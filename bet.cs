using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using System;

public class BuildErrorTracker : IPostprocessBuildWithReport, EditorWindow
{
    // 実行順序（他の処理より後に実行）
    public int callbackOrder => 999;
    
    // GUI関連の変数
    private static BuildErrorTracker window;
    private Vector2 scrollPosition;
    private List<ErrorInfo> detectedErrors = new List<ErrorInfo>();
    private bool autoScanEnabled = true;
    private float scanInterval = 60f; // 1分ごと
    private double lastScanTime;
    private int totalErrorsFound;
    private bool isScanning;
    
    // エラー情報の構造体
    [System.Serializable]
    public class ErrorInfo
    {
        public string message;
        public string objectPath;
        public ErrorType type;
        public double timestamp;
        public string severity;
    }
    
    public enum ErrorType
    {
        MissingScript,
        MissingReference,
        CompilationError,
        BuildError,
        Warning
    }

    [MenuItem("Tools/VRC改変支援/BET Sys")]
    public static void ShowWindow()
    {
        window = GetWindow<BuildErrorTracker>("VRCエラー検出");
        window.minSize = new Vector2(400, 300);
        window.Show();
    }
    
    public void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        lastScanTime = EditorApplication.timeSinceStartup;
    }
    
    public void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }
    
    private void OnEditorUpdate()
    {
        if (autoScanEnabled && !isScanning)
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - lastScanTime >= scanInterval)
            {
                lastScanTime = currentTime;
                EditorApplication.delayCall += PerformAutoScan;
            }
        }
    }
    
    private void PerformAutoScan()
    {
        if (isScanning) return;
        
        isScanning = true;
        detectedErrors.Clear();
        
        // 高精度なエラー検出
        PerformHighPrecisionScan();
        
        // ウィンドウを更新
        if (window != null)
        {
            window.Repaint();
        }
        
        // エラーが見つかった場合に通知
        if (detectedErrors.Count > 0)
        {
            ShowErrorNotification();
        }
        
        isScanning = false;
    }
    
    private void PerformHighPrecisionScan()
    {
        // Missing Scriptの検出
        AnalyzeMissingScripts();
        
        // Missing Referenceの検出
        AnalyzeMissingReferences();
        
        // コンパイルエラーの検出
        CheckCompilationErrors();
        
        // プレハブの問題検出
        AnalyzePrefabIssues();
    }
    
    private void AnalyzeMissingScripts()
    {
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        foreach (var obj in allObjects)
        {
            var components = obj.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    var error = new ErrorInfo
                    {
                        message = $"Missing Script Component detected on {obj.name}",
                        objectPath = GetGameObjectPath(obj),
                        type = ErrorType.MissingScript,
                        timestamp = EditorApplication.timeSinceStartup,
                        severity = "High"
                    };
                    detectedErrors.Add(error);
                }
            }
        }
    }
    
    private void AnalyzeMissingReferences()
    {
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        foreach (var obj in allObjects)
        {
            var components = obj.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null) continue;
                
                var serializedObject = new SerializedObject(component);
                var property = serializedObject.GetIterator();
                
                while (property.NextVisible(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference && 
                        property.objectReferenceValue == null && 
                        !string.IsNullOrEmpty(property.objectReferenceInstanceIDString))
                    {
                        var error = new ErrorInfo
                        {
                            message = $"Missing Reference in {component.GetType().Name}.{property.name}",
                            objectPath = GetGameObjectPath(obj),
                            type = ErrorType.MissingReference,
                            timestamp = EditorApplication.timeSinceStartup,
                            severity = "Medium"
                        };
                        detectedErrors.Add(error);
                    }
                }
            }
        }
    }
    
    private void CheckCompilationErrors()
    {
        var compileErrors = AssetDatabase.FindAssets("t:MonoScript")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(path => AssetDatabase.LoadAssetAtPath<MonoScript>(path))
            .Where(script => script != null);
            
        foreach (var script in compileErrors)
        {
            // スクリプトのコンパイル状態をチェック
            if (!EditorUtility.IsPersistent(script))
            {
                var error = new ErrorInfo
                {
                    message = $"Compilation issue in script: {script.name}",
                    objectPath = AssetDatabase.GetAssetPath(script),
                    type = ErrorType.CompilationError,
                    timestamp = EditorApplication.timeSinceStartup,
                    severity = "High"
                };
                detectedErrors.Add(error);
            }
        }
    }
    
    private void AnalyzePrefabIssues()
    {
        string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();
            
        foreach (string path in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            
            // プレハブ内のMissing Scriptをチェック
            var components = prefab.GetComponentsInChildren<Component>();
            foreach (var component in components)
            {
                if (component == null)
                {
                    var error = new ErrorInfo
                    {
                        message = $"Missing Script in prefab: {prefab.name}",
                        objectPath = path,
                        type = ErrorType.MissingScript,
                        timestamp = EditorApplication.timeSinceStartup,
                        severity = "High"
                    };
                    detectedErrors.Add(error);
                }
            }
        }
    }
    
    private void ShowErrorNotification()
    {
        string message = $"{detectedErrors.Count}個のエラーが検出されました！\n";
        message += "詳細はGUIウィンドウで確認してください。";
        
        EditorApplication.Beep();
        EditorUtility.DisplayDialog("エラー検出通知", message, "OK");
    }
    
    private void OnGUI()
    {
        DrawHeader();
        DrawControls();
        DrawErrorList();
        DrawFooter();
    }
    
    private void DrawHeader()
    {
        EditorGUILayout.Space();
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter
        };
        
        EditorGUILayout.LabelField("VRCエラー検出ツール", headerStyle);
        EditorGUILayout.Space();
    }
    
    private void DrawControls()
    {
        EditorGUILayout.BeginVertical("box");
        
        EditorGUILayout.LabelField("スキャン設定", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        autoScanEnabled = EditorGUILayout.Toggle("自動スキャン", autoScanEnabled);
        if (GUILayout.Button("今すぐスキャン", GUILayout.Width(100)))
        {
            PerformAutoScan();
        }
        EditorGUILayout.EndHorizontal();
        
        if (autoScanEnabled)
        {
            scanInterval = EditorGUILayout.Slider("スキャン間隔（秒）", scanInterval, 10f, 300f);
        }
        
        EditorGUILayout.Space();
        
        // ステータス表示
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("状態:", isScanning ? "スキャン中..." : "待機中", 
            isScanning ? new GUIStyle(EditorStyles.label) { normal = { textColor = Color.yellow } } : EditorStyles.label);
        EditorGUILayout.LabelField($"検出エラー数: {detectedErrors.Count}");
        EditorGUILayout.EndHorizontal();
        
        if (GUILayout.Button("エラーリストをクリア"))
        {
            detectedErrors.Clear();
            totalErrorsFound = 0;
        }
        
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }
    
    private void DrawErrorList()
    {
        if (detectedErrors.Count == 0)
        {
            EditorGUILayout.HelpBox("エラーは検出されていません。", MessageType.Info);
            return;
        }
        
        EditorGUILayout.LabelField($"検出されたエラー ({detectedErrors.Count}件)", EditorStyles.boldLabel);
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        for (int i = 0; i < detectedErrors.Count; i++)
        {
            DrawErrorItem(detectedErrors[i], i);
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    private void DrawErrorItem(ErrorInfo error, int index)
    {
        EditorGUILayout.BeginVertical("box");
        
        // 重要度に応じた色分け
        Color originalColor = GUI.color;
        switch (error.severity)
        {
            case "High":
                GUI.color = Color.red;
                break;
            case "Medium":
                GUI.color = Color.yellow;
                break;
            default:
                GUI.color = Color.white;
                break;
        }
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"[{error.type}] {error.message}", EditorStyles.boldLabel);
        if (GUILayout.Button("選択", GUILayout.Width(50)))
        {
            SelectObject(error.objectPath);
        }
        EditorGUILayout.EndHorizontal();
        
        GUI.color = originalColor;
        
        EditorGUILayout.LabelField($"パス: {error.objectPath}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"重要度: {error.severity} | 時刻: {System.DateTime.FromBinary((long)error.timestamp):HH:mm:ss}", 
            EditorStyles.miniLabel);
        
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }
    
    private void SelectObject(string objectPath)
    {
        if (objectPath.StartsWith("Assets/"))
        {
            // アセットの場合
            var obj = AssetDatabase.LoadMainAssetAtPath(objectPath);
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
        else
        {
            // シーン内のオブジェクトの場合
            var obj = GameObject.Find(objectPath);
            if (obj != null)
            {
                Selection.activeGameObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }
    }
    
    private void DrawFooter()
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("ログをエクスポート"))
        {
            ExportErrorLog();
        }
        
        if (GUILayout.Button("設定をリセット"))
        {
            ResetSettings();
        }
        
        EditorGUILayout.EndHorizontal();
        
        // 次回スキャンまでの時間を表示
        if (autoScanEnabled && !isScanning)
        {
            double timeUntilNextScan = scanInterval - (EditorApplication.timeSinceStartup - lastScanTime);
            EditorGUILayout.LabelField($"次回スキャンまで: {Mathf.Max(0, (float)timeUntilNextScan):F1}秒", EditorStyles.miniLabel);
        }
    }
    
    private void ExportErrorLog()
    {
        string path = EditorUtility.SaveFilePanel("エラーログを保存", "", "error_log.txt", "txt");
        if (!string.IsNullOrEmpty(path))
        {
            string log = "VRCエラー検出ログ\n";
            log += $"生成時刻: {System.DateTime.Now}\n";
            log += $"検出エラー数: {detectedErrors.Count}\n\n";
            
            foreach (var error in detectedErrors)
            {
                log += $"[{error.type}] {error.message}\n";
                log += $"パス: {error.objectPath}\n";
                log += $"重要度: {error.severity}\n";
                log += $"時刻: {System.DateTime.FromBinary((long)error.timestamp)}\n\n";
            }
            
            System.IO.File.WriteAllText(path, log);
            EditorUtility.DisplayDialog("エクスポート完了", "エラーログが保存されました。", "OK");
        }
    }
    
    private void ResetSettings()
    {
        autoScanEnabled = true;
        scanInterval = 60f;
        detectedErrors.Clear();
        totalErrorsFound = 0;
        lastScanTime = EditorApplication.timeSinceStartup;
    }
    
    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.result == BuildResult.Failed)
        {
            Debug.LogError("--- [VRC改変支援] ビルド失敗の詳細を分析します ---");
            
            // ステップごとのログを確認
            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                    {
                        Debug.LogWarning($"【検知されたエラー】: {message.content}");
                        
                        // GUIにもエラーを追加
                        var error = new ErrorInfo
                        {
                            message = message.content,
                            objectPath = "Build Process",
                            type = ErrorType.BuildError,
                            timestamp = EditorApplication.timeSinceStartup,
                            severity = "High"
                        };
                        detectedErrors.Add(error);
                    }
                }
            }

            // 特に怪しいアセットを特定
            AnalyzeMissingAssets();
        }
    }

    private void AnalyzeMissingAssets()
    {
        // ヒエラルキー内の全オブジェクトからMissingを探す
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        foreach (var obj in allObjects)
        {
            var components = obj.GetComponents<Component>();
            if (components.Any(c => c == null))
            {
                Debug.LogError($"<color=red>【原因候補】</color> オブジェクト '{obj.name}' にMissing Scriptがあります。パス: {GetGameObjectPath(obj)}");
                
                // GUIにもエラーを追加
                var error = new ErrorInfo
                {
                    message = $"Missing Script on '{obj.name}'",
                    objectPath = GetGameObjectPath(obj),
                    type = ErrorType.MissingScript,
                    timestamp = EditorApplication.timeSinceStartup,
                    severity = "High"
                };
                detectedErrors.Add(error);
            }
        }
    }

    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = obj.name + "/" + path;
        }
        return path;
    }
}