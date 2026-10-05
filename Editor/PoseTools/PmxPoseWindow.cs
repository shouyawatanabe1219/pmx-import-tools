using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PmxImport.Editor
{
    public sealed class PmxPoseWindow : EditorWindow
    {
        [SerializeField] GameObject model;
        [SerializeField] PmxPoseDocument document;
        [SerializeField] int frame;
        [SerializeField] int boneIndex;
        [SerializeField] int expressionIndex;
        [SerializeField] bool sceneHandles = true;
        [SerializeField] bool showAllBones;
        [SerializeField] bool autoRecord = true;
        [SerializeField] bool limbIK = true;
        bool rigChanged;
        PopupField<string> boneChoice;
        System.Collections.Generic.List<string> boneNames;
        PmxPoseKey baseline;
        Transform[] previewBones;
        SkinnedMeshRenderer previewMesh;
        int[] previewExpressionIndices;
        bool previewPlaying;
        double previewStart;
        int previewStartFrame;
        Transform[] bones;
        SkinnedMeshRenderer mesh;
        SliderInt frameSlider;
        Vector3Field positionField, rotationField, scaleField;
        Slider expressionField;
        Label status;
        VisualElement editingArea, keyArea;

        [MenuItem("Tools/PMX/ポーズ・アニメーション作成")]
        public static PmxPoseWindow Open()
        {
            var window = GetWindow<PmxPoseWindow>("PMX ポーズ作成");
            window.minSize = new Vector2(440, 600);
            return window;
        }

        void OnEnable()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += EndPreview;
            EditorSceneManager.sceneSaving += SceneSaving;
            SceneView.duringSceneGui += SceneGUI;
            Undo.undoRedoPerformed += UndoRedo;
        }

        void OnDisable()
        {
            EndPreview();
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= EndPreview;
            EditorSceneManager.sceneSaving -= SceneSaving;
            SceneView.duringSceneGui -= SceneGUI;
            Undo.undoRedoPerformed -= UndoRedo;
        }

        void SceneSaving(Scene scene, string path) { EndPreview(); }
        void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) EndPreview();
            if (editingArea != null) editingArea.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode && document != null);
        }
        void UndoRedo() { EndPreview(); Rebuild(); if (document != null && model != null) Preview(frame); }

        public void SetTarget(GameObject target)
        {
            EndPreview(); model = target; document = null; frame = 0;
            Rebuild();
        }

        public void SetDocument(PmxPoseDocument draft)
        {
            EndPreview(); document = draft; frame = 0; Rebuild();
        }

        public void CreateGUI() { Rebuild(); }

        void Rebuild()
        {
            rootVisualElement.Clear();
            string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            string directory = System.IO.Path.GetDirectoryName(scriptPath).Replace('\\', '/');
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(directory + "/PmxPoseWindow.uxml");
            if (template == null) { rootVisualElement.Add(new Label("PmxPoseWindow.uxml / .uss を同じフォルダーに配置してください。")); return; }
            template.CloneTree(rootVisualElement);
            status = rootVisualElement.Q<Label>("statusLabel");
            editingArea = rootVisualElement.Q("editingArea");
            var modelArea = rootVisualElement.Q("modelArea");
            var modelField = new ObjectField("シーンのモデル") { objectType = typeof(GameObject), allowSceneObjects = true, value = model };
            modelField.RegisterValueChangedCallback(e => SetTarget(e.newValue as GameObject));
            modelArea.Add(modelField);
            Row(modelArea, Button("選択中のモデルを使う", () => {
                var target = Selection.activeGameObject;
                while (target != null && target.GetComponent<PmxPhysicsController>() == null && target.GetComponent<PmxFaceController>() == null)
                    target = target.transform.parent != null ? target.transform.parent.gameObject : null;
                if (target != null) SetTarget(target); else Message("HierarchyでPMXモデルのルートを選択してください。");
            }));

            var documentArea = rootVisualElement.Q("documentArea");
            var docField = new ObjectField("作業データ (.asset)") { objectType = typeof(PmxPoseDocument), allowSceneObjects = false, value = document };
            docField.RegisterValueChangedCallback(e => { EndPreview(); document = e.newValue as PmxPoseDocument; Rebuild(); });
            documentArea.Add(docField);
            Row(documentArea, Button("新しい作業データ", NewDocument), Button("データを保存", () => {
                if (document != null) { EditorUtility.SetDirty(document); AssetDatabase.SaveAssets(); Message("作業データを保存しました。"); }
            }));
            if (EditorApplication.isPlayingOrWillChangePlaymode) { editingArea.SetEnabled(false); Message("編集はPlayモードを停止してから行ってください。"); return; }
            if (model == null || !model.scene.IsValid() || EditorUtility.IsPersistent(model))
            { editingArea.SetEnabled(false); Message("Hierarchyにあるモデルのルートを指定してください。"); return; }
            if (document == null) { editingArea.SetEnabled(false); Message("「新しい作業データ」で現在のポーズをフレーム0に記録して始めます。"); return; }
            try
            {
                PmxPoseUtility.Validate(document, model);
                bones = document.bonePaths.Select(p => model.transform.Find(p)).ToArray();
                mesh = PmxPoseUtility.Mesh(document, model);
                frame = Mathf.Clamp(frame, 0, document.endFrame);
                boneIndex = Mathf.Clamp(boneIndex, 0, bones.Length - 1);
                expressionIndex = Mathf.Clamp(expressionIndex, 0, document.expressionNames.Length - 1);
                BuildTimeline(); BuildBones(); BuildExpressions(); BuildKeys(); BuildExport();
                Message("ボーンを調整したら「このフレームに記録」を押してください。記録前の変更はフレーム移動で戻ります。");
            }
            catch (Exception e) { editingArea.SetEnabled(false); Message(e.Message); }
        }

        void NewDocument()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || model == null || !model.scene.IsValid() || EditorUtility.IsPersistent(model))
            { Message("Playモードを停止し、シーンのモデルを指定してください。"); return; }
            try { PmxPoseUtility.Bones(model); } catch (Exception e) { Message(e.Message); return; }
            string path = EditorUtility.SaveFilePanelInProject("作業データを保存", "MyMotion", "asset", "保存先を選んでください。");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) { Message("別の名前で保存してください。"); return; }
            EndPreview();
            document = CreateInstance<PmxPoseDocument>(); document.name = System.IO.Path.GetFileNameWithoutExtension(path);
            PmxPoseUtility.Initialize(document, model); AssetDatabase.CreateAsset(document, path); AssetDatabase.SaveAssets();
            frame = 0; Rebuild();
        }

        void BuildTimeline()
        {
            var area = rootVisualElement.Q("timelineArea"); Title(area, "1. フレームと再生");
            var fps = new IntegerField("FPS (1〜120)") { value = document.frameRate };
            fps.RegisterValueChangedCallback(e => { Undo.RecordObject(document, "FPSを変更"); document.frameRate = Mathf.Clamp(e.newValue, 1, 120); fps.SetValueWithoutNotify(document.frameRate); EditorUtility.SetDirty(document); });
            area.Add(fps);
            var end = new IntegerField("終了フレーム (最大3600)") { value = document.endFrame };
            end.RegisterValueChangedCallback(e => {
                int value = Mathf.Clamp(e.newValue, Mathf.Max(1, document.keys.Max(k => k.frame)), 3600);
                Undo.RecordObject(document, "終了フレームを変更"); document.endFrame = value; EditorUtility.SetDirty(document);
                end.SetValueWithoutNotify(value); frameSlider.highValue = value;
            }); area.Add(end);
            frameSlider = new SliderInt("現在のフレーム", 0, document.endFrame) { value = frame, showInputField = true };
            frameSlider.RegisterValueChangedCallback(e => { previewPlaying = false; Preview(e.newValue); }); area.Add(frameSlider);
            Row(area, Button("プレビュー再生", () => {
                if (!BeginPreview()) return;
                previewStartFrame = frame >= document.endFrame ? 0 : frame;
                previewStart = EditorApplication.timeSinceStartup; previewPlaying = true;
            }), Button("一時停止", () => previewPlaying = false), Button("元の状態に戻す", () => { EndPreview(); RefreshFields(); Message("プレビュー前の状態に戻しました。"); }));
        }

        void BuildBones()
        {
            var area = rootVisualElement.Q("boneArea"); Title(area, "2. ボーン調整");
            boneNames = bones.Select((t, i) => i + ": " + t.name).ToList();
            boneChoice = new PopupField<string>("ボーン", boneNames, boneIndex);
            boneChoice.RegisterValueChangedCallback(e => { boneIndex = boneNames.IndexOf(e.newValue); RefreshFields(); SceneView.RepaintAll(); }); area.Add(boneChoice);
            positionField = new Vector3Field("ローカル位置"); rotationField = new Vector3Field("ローカル回転 (度)"); scaleField = new Vector3Field("ローカル大きさ");
            positionField.RegisterValueChangedCallback(e => EditBone(t => t.localPosition = e.newValue));
            rotationField.RegisterValueChangedCallback(e => EditBone(t => t.localRotation = Quaternion.Euler(e.newValue)));
            scaleField.RegisterValueChangedCallback(e => EditBone(t => t.localScale = new Vector3(Mathf.Max(.001f, e.newValue.x), Mathf.Max(.001f, e.newValue.y), Mathf.Max(.001f, e.newValue.z))));
            area.Add(positionField); area.Add(rotationField); area.Add(scaleField);
            var handles = new Toggle("Sceneビューでリグを直接操作") { value = sceneHandles };
            handles.RegisterValueChangedCallback(e => { sceneHandles = e.newValue; SceneView.RepaintAll(); }); area.Add(handles);
            var allBones = new Toggle("指・髪・補助ボーンも表示") { value = showAllBones };
            allBones.RegisterValueChangedCallback(e => { showAllBones = e.newValue; SceneView.RepaintAll(); }); area.Add(allBones);
            var automatic = new Toggle("操作したポーズを現在のフレームへ自動記録") { value = autoRecord };
            automatic.RegisterValueChangedCallback(e => autoRecord = e.newValue); area.Add(automatic);
            var ik = new Toggle("手首・足首の移動で腕・脚も曲げる (IK)") { value = limbIK };
            ik.RegisterValueChangedCallback(e => limbIK = e.newValue); area.Add(ik);
            Row(area, Button("移動 (W)", () => Tools.current = Tool.Move), Button("回転 (E)", () => Tools.current = Tool.Rotate),
                Button("拡縮 (R)", () => Tools.current = Tool.Scale), Button("リグ全体を表示", FrameRig));
            area.Add(new Label("Sceneの点をクリックしてボーンを選択 → ハンドルをドラッグ。手足を曲げるには回転を使います。"));
            Row(area, Button("ボーンをSceneで表示", () => {
                Selection.activeGameObject = model; SceneView.lastActiveSceneView?.Frame(new Bounds(bones[boneIndex].position, Vector3.one * .3f), false);
            }), Button("フレーム0のポーズに戻す", () => {
                if (BeginPreview()) { PmxPoseUtility.Apply(document, model, PmxPoseUtility.Evaluate(document, 0)); RefreshFields(); SceneView.RepaintAll(); }
            })); RefreshFields();
        }

        void BuildExpressions()
        {
            var area = rootVisualElement.Q("expressionArea"); Title(area, "3. 表情");
            if (document.expressionNames.Length == 0) { area.Add(new Label("このモデルには頂点モーフがありません。")); return; }
            var names = document.expressionNames.ToList();
            var choice = new PopupField<string>("表情", names, expressionIndex);
            choice.RegisterValueChangedCallback(e => { expressionIndex = names.IndexOf(e.newValue); RefreshFields(); }); area.Add(choice);
            expressionField = new Slider("強さ (%)", 0, 100) { showInputField = true };
            expressionField.RegisterValueChangedCallback(e => {
                previewPlaying = false; if (!BeginPreview()) return;
                int index = mesh.sharedMesh.GetBlendShapeIndex(document.expressionNames[expressionIndex]); mesh.SetBlendShapeWeight(index, e.newValue); SceneView.RepaintAll();
                if (autoRecord) RecordCurrentFrame();
            }); area.Add(expressionField); RefreshFields();
        }

        void BuildKeys()
        {
            keyArea = rootVisualElement.Q("keyArea"); keyArea.Clear(); Title(keyArea, "4. ポーズを記録");
            Row(keyArea, Button("このフレームに記録", () => {
                RecordCurrentFrame();
            }));
            foreach (var key in document.keys.OrderBy(k => k.frame).ToArray())
            {
                int at = key.frame;
                var row = new VisualElement(); row.AddToClassList("pose-key");
                row.Add(Button(at + " f  /  " + ((float)at / document.frameRate).ToString("0.00") + " 秒", () => { previewPlaying = false; Preview(at); }));
                var delete = Button("削除", () => {
                    Undo.RecordObject(document, "キーを削除"); document.keys.RemoveAll(k => k.frame == at); EditorUtility.SetDirty(document); BuildKeys();
                }); delete.SetEnabled(document.keys.Count > 1 && at != 0); row.Add(delete); keyArea.Add(row);
            }
        }

        void BuildExport()
        {
            var area = rootVisualElement.Q("exportArea"); Title(area, "5. 保存・再生");
            var loop = new Toggle("ループ再生") { value = document.loop };
            loop.RegisterValueChangedCallback(e => { Undo.RecordObject(document, "ループ設定"); document.loop = e.newValue; EditorUtility.SetDirty(document); }); area.Add(loop);
            var physics = new Toggle("髪・スカートなど物理ボーンは書き出さない") { value = document.preservePhysics };
            physics.RegisterValueChangedCallback(e => { Undo.RecordObject(document, "物理設定"); document.preservePhysics = e.newValue; EditorUtility.SetDirty(document); }); area.Add(physics);
            area.Add(new Label("ループさせる場合は、終了フレームにも開始ポーズを記録してください。"));
            Row(area, Button(".anim に書き出してモデルへ設定", Export), Button("現在の調整をシーンに確定", Commit));
            area.Add(new Label("未確定のプレビューは、ウィンドウを閉じる・シーン保存・Play開始時に元へ戻ります。"));
        }

        bool BeginPreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            try
            {
                PmxPoseUtility.Validate(document, model);
                if (AnimationMode.InAnimationMode()) throw new InvalidOperationException("UnityのAnimationウィンドウのプレビューを停止してください。");
                if (baseline == null)
                {
                    baseline = PmxPoseUtility.Capture(document, model, 0);
                    previewBones = document.bonePaths.Select(p => model.transform.Find(p)).ToArray();
                    previewMesh = PmxPoseUtility.Mesh(document, model);
                    previewExpressionIndices = document.expressionNames.Select(previewMesh.sharedMesh.GetBlendShapeIndex).ToArray();
                }
                return true;
            }
            catch (Exception e) { Message(e.Message); return false; }
        }

        void EndPreview()
        {
            previewPlaying = false;
            rigChanged = false;
            if (baseline != null && previewBones != null)
            {
                for (int i = 0; i < previewBones.Length; i++)
                {
                    var bone = previewBones[i]; if (bone == null) continue;
                    bone.localPosition = baseline.positions[i]; bone.localRotation = baseline.rotations[i]; bone.localScale = baseline.scales[i];
                }
                if (previewMesh != null && previewMesh.sharedMesh != null)
                    for (int i = 0; i < previewExpressionIndices.Length; i++)
                        if (previewExpressionIndices[i] >= 0 && previewExpressionIndices[i] < previewMesh.sharedMesh.blendShapeCount)
                            previewMesh.SetBlendShapeWeight(previewExpressionIndices[i], baseline.expressions[i]);
            }
            baseline = null; previewBones = null; previewMesh = null; previewExpressionIndices = null; SceneView.RepaintAll();
        }

        void Preview(int at)
        {
            if (!BeginPreview()) return;
            frame = Mathf.Clamp(at, 0, document.endFrame);
            PmxPoseUtility.Apply(document, model, PmxPoseUtility.Evaluate(document, frame));
            frameSlider?.SetValueWithoutNotify(frame); RefreshFields(); SceneView.RepaintAll();
        }

        void EditBone(Action<Transform> action)
        {
            previewPlaying = false; if (!BeginPreview()) return;
            action(bones[boneIndex]); RefreshFields(); SceneView.RepaintAll();
            if (autoRecord) RecordCurrentFrame();
        }

        public bool SelectRigBone(Transform bone)
        {
            if (bones == null || bone == null) return false;
            int index = Array.IndexOf(bones, bone);
            if (index < 0) return false;
            boneIndex = index;
            boneChoice?.SetValueWithoutNotify(boneNames[index]);
            RefreshFields(); SceneView.RepaintAll();
            Message(bone.name + " を選択しました。W:移動 / E:回転 / R:拡縮。");
            return true;
        }

        public void RecordCurrentFrame()
        {
            previewPlaying = false; if (!BeginPreview()) return;
            Undo.RecordObject(document, "リグのポーズを記録");
            PmxPoseUtility.Record(document, model, frame);
            BuildKeys();
            Message(frame + "フレームのポーズを記録しました。Undoで戻せます。");
        }

        void FrameRig()
        {
            if (bones == null || bones.Length == 0 || model == null) return;
            var bounds = new Bounds(bones[0].position, Vector3.one * .1f);
            foreach (var bone in bones) if (bone != null) bounds.Encapsulate(bone.position);
            SceneView.lastActiveSceneView?.Frame(bounds, false);
            SceneView.RepaintAll();
        }

        void RefreshFields()
        {
            if (bones == null || bones.Length == 0 || bones[boneIndex] == null) return;
            var t = bones[boneIndex]; positionField?.SetValueWithoutNotify(t.localPosition);
            rotationField?.SetValueWithoutNotify(t.localEulerAngles); scaleField?.SetValueWithoutNotify(t.localScale);
            if (expressionField != null && mesh != null && document.expressionNames.Length > 0)
                expressionField.SetValueWithoutNotify(mesh.GetBlendShapeWeight(mesh.sharedMesh.GetBlendShapeIndex(document.expressionNames[expressionIndex])));
        }

        void Tick()
        {
            if (!previewPlaying || document == null || model == null) return;
            int next = previewStartFrame + Mathf.FloorToInt((float)(EditorApplication.timeSinceStartup - previewStart) * document.frameRate);
            if (next > document.endFrame)
            {
                if (document.loop) next %= document.endFrame + 1;
                else { next = document.endFrame; previewPlaying = false; }
            }
            if (next != frame) Preview(next);
        }

        void SceneGUI(SceneView view)
        {
            if (!sceneHandles || model == null || document == null || bones == null || EditorApplication.isPlayingOrWillChangePlaymode || previewPlaying) return;
            var bone = bones[boneIndex]; if (bone == null) return;
            var oldDepth = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            try
            {
                var visible = PmxSceneRig.VisibleBones(bones, showAllBones, bone);
                var set = new System.Collections.Generic.HashSet<Transform>(visible);
                using (new Handles.DrawingScope(new Color(.15f, .85f, .8f, .85f)))
                {
                    foreach (var node in visible)
                    {
                        var parent = node.parent;
                        while (parent != null && parent != model.transform && !set.Contains(parent)) parent = parent.parent;
                        if (parent != null && set.Contains(parent)) Handles.DrawLine(parent.position, node.position, 2f);
                    }
                }
                foreach (var node in visible)
                {
                    float size = HandleUtility.GetHandleSize(node.position) * (node == bone ? .065f : .045f);
                    using (new Handles.DrawingScope(node == bone ? new Color(1f, .7f, .15f) : new Color(.15f, .9f, .85f)))
                        if (Handles.Button(node.position, Quaternion.identity, size, size * 1.3f, Handles.SphereHandleCap))
                            SelectRigBone(node);
                }
                bone = bones[boneIndex];
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                Handles.Label(bone.position, bone.name);
                EditorGUI.BeginChangeCheck();
                if (Tools.current == Tool.Move)
                {
                    var p = Handles.PositionHandle(bone.position, Tools.pivotRotation == PivotRotation.Local ? bone.rotation : Quaternion.identity);
                    if (EditorGUI.EndChangeCheck() && BeginPreview())
                    {
                        if (limbIK && PmxSceneRig.TryGetLimb(bones, bone, out var upper, out var middle))
                        {
                            bool leg = bone.name.Contains("足") || bone.name.EndsWith("Foot");
                            PmxSceneRig.SolveLimb(upper, middle, bone, p, model.transform.TransformDirection(leg ? Vector3.forward : Vector3.back));
                        }
                        else bone.position = p;
                        rigChanged = true; RefreshFields();
                    }
                }
                else if (Tools.current == Tool.Scale)
                {
                    var s = Handles.ScaleHandle(bone.localScale, bone.position, bone.rotation, HandleUtility.GetHandleSize(bone.position));
                    if (EditorGUI.EndChangeCheck() && BeginPreview())
                    {
                        bone.localScale = new Vector3(Mathf.Max(.001f, s.x), Mathf.Max(.001f, s.y), Mathf.Max(.001f, s.z));
                        rigChanged = true; RefreshFields();
                    }
                }
                else if (Tools.current == Tool.Rotate)
                {
                    var q = Handles.RotationHandle(bone.rotation, bone.position);
                    if (EditorGUI.EndChangeCheck() && BeginPreview()) { bone.rotation = q; rigChanged = true; RefreshFields(); }
                }
                else EditorGUI.EndChangeCheck();
                if (rigChanged && Event.current.rawType == EventType.MouseUp)
                {
                    rigChanged = false;
                    if (autoRecord) RecordCurrentFrame();
                    else Message("リグを調整しました。「このフレームに記録」で保存してください。");
                }
            }
            finally { Handles.zTest = oldDepth; }
        }

        void Commit()
        {
            if (!BeginPreview()) return;
            previewPlaying = false;
            var desired = PmxPoseUtility.Capture(document, model, frame);
            EndPreview();
            Undo.RecordObjects(bones.Cast<UnityEngine.Object>().Append(mesh).ToArray(), "PMXモデル調整を確定");
            PmxPoseUtility.Apply(document, model, desired);
            foreach (var bone in bones) PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mesh);
            EditorSceneManager.MarkSceneDirty(model.scene);
            Message("調整をシーンに確定しました。Undoで戻せます。元のPMXファイルは変更されません。");
        }

        void Export()
        {
            if (document == null) return;
            string path = EditorUtility.SaveFilePanelInProject("アニメーションを書き出す", document.name, "anim", "保存先を選んでください。");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) { Message("別の名前で保存してください。"); return; }
            try
            {
                var clip = PmxPoseUtility.Export(document, model);
                EndPreview(); AssetDatabase.CreateAsset(clip, path); AssetDatabase.SaveAssets();
                var player = model.GetComponent<PmxAnimationPlayer>();
                if (player == null) player = Undo.AddComponent<PmxAnimationPlayer>(model);
                Undo.RecordObject(player, "アニメーション設定"); player.clip = clip; player.loop = document.loop;
                PrefabUtility.RecordPrefabInstancePropertyModifications(player); EditorSceneManager.MarkSceneDirty(model.scene);
                Message("書き出しました。シーンを保存してPlayすると再生します。");
            }
            catch (Exception e) { Message(e.Message); }
        }

        void Message(string text) { if (status != null) status.text = text; }
        static Button Button(string text, Action action) => new Button(action) { text = text };
        static void Row(VisualElement parent, params VisualElement[] elements)
        {
            var row = new VisualElement(); row.AddToClassList("pose-row"); foreach (var element in elements) row.Add(element); parent.Add(row);
        }
        static void Title(VisualElement parent, string text)
        {
            var title = new Label(text); title.AddToClassList("pose-section-title"); parent.Add(title);
        }
    }
}
