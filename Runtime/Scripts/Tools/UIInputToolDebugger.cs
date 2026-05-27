using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

public class UIInputToolDebugger : MonoBehaviour
{
    public static UIInputToolDebugger Instance;

    [SerializeField] private UIDocument _uiDocument;
    [SerializeField] private bool _enabled = false;

    private VisualElement _root;
    private VisualElement _controllersContainer;

    private List<DefaultController> _controllers = new List<DefaultController>();
    private List<VisualElement> _pickedElements = new List<VisualElement>();
    private Coroutine _pollRoutine;

    private bool _isInitialized;
    private bool _isActive;

    private void Awake()
    {
        Instance = this;

        if (_uiDocument == null)
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        if (_uiDocument != null)
        {
            _uiDocument.sortingOrder = 9999;
            _root = _uiDocument.rootVisualElement;
            _controllersContainer = _root.Q<VisualElement>("controllers-container");
        }

        _isInitialized = true;
        ApplyEnabledState();
    }

    private void Start()
    {
        // The visibility is now controlled strictly by DebugInfoDisplay.cs
    }

    public void ToggleDebugger(bool visible)
    {
        _enabled = visible;
        ApplyEnabledState();
    }

    private void OnEnable()
    {
        ApplyEnabledState();
    }

    private void OnDisable()
    {
        if (!_isInitialized) return;
        DeactivateTool();
    }

    private void Update()
    {
        if (_enabled != _isActive)
        {
            ApplyEnabledState();
        }
    }

    private void ApplyEnabledState()
    {
        if (!_isInitialized) return;

        if (_enabled)
        {
            ActivateTool();
        }
        else
        {
            DeactivateTool();
        }
    }

    private void ActivateTool()
    {
        if (_isActive) return;
        _isActive = true;

        if (_uiDocument != null)
        {
            _uiDocument.rootVisualElement.style.display = DisplayStyle.Flex;
        }

        if (_pollRoutine == null)
        {
            _pollRoutine = StartCoroutine(PollForControllersRoutine());
        }

        RefreshControllers();
    }

    private void DeactivateTool()
    {
        if (!_isActive) return;
        _isActive = false;
        
        if (_uiDocument != null && _uiDocument.rootVisualElement != null)
        {
            _uiDocument.rootVisualElement.style.display = DisplayStyle.None;
        }

        if (_pollRoutine != null)
        {
            if (isActiveAndEnabled)
            {
                StopCoroutine(_pollRoutine);
            }
            _pollRoutine = null;
        }

        _controllers?.Clear();
        _controllersContainer?.Clear();
    }

    private void RefreshControllers()
    {
        var foundControllers = FindObjectsOfType<DefaultController>()
            .Where(controller => controller != null && controller.IsTracked)
            .ToList();

        bool hasNewController = false;

        foreach (var controller in foundControllers)
        {
            if (!_controllers.Contains(controller))
            {
                _controllers.Add(controller);
                hasNewController = true;
            }
        }

        if (hasNewController)
        {
            BuildUI();
        }
    }

    private IEnumerator PollForControllersRoutine()
    {
        while (_enabled)
        {
            RefreshControllers();
            yield return new WaitForSeconds(1f);
        }

        _pollRoutine = null;
    }

    private bool AreListsEqual(List<DefaultController> list1, List<DefaultController> list2)
    {
        if (list1.Count != list2.Count) return false;

        for (int i = 0; i < list1.Count; i++)
        {
            if (list1[i] != list2[i]) return false;
        }

        return true;
    }

    public void Init(List<DefaultController> controllers)
    {
        _controllers = (controllers ?? new List<DefaultController>())
            .Where(controller => controller != null && controller.IsTracked)
            .ToList();

        if (_enabled)
        {
            BuildUI();
        }
    }

    private void BuildUI()
    {
        if (_root == null)
        {
            if (_uiDocument == null) return;
            _root = _uiDocument.rootVisualElement;
        }

        _controllersContainer = _root.Q<VisualElement>("controllers-container");
        if (_controllersContainer == null) return;

        _controllersContainer.Clear();

        AddStartOverCalibrationButton(_controllersContainer);

        // Bouton global pour rafraîchir les panels de raccourcis
        // (les raccourcis sont souvent assignés après le BuildUI initial)
        Action refreshAction = () => BuildUI();
        var btnRefresh = new Button(refreshAction)
        {
            text = "🔄 Actualiser les raccourcis",
            userData = refreshAction
        };
        btnRefresh.style.height = 36;
        btnRefresh.style.fontSize = 13;
        btnRefresh.style.marginBottom = 10;
        btnRefresh.style.backgroundColor = new Color(0.18f, 0.25f, 0.45f, 0.95f);
        btnRefresh.style.color = Color.white;
        _controllersContainer.Add(btnRefresh);

        foreach (var controller in _controllers.Where(controller => controller != null && controller.IsTracked))
        {
            var row = new VisualElement();
            row.AddToClassList("controller-row");

            row.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
            row.style.paddingBottom = 12;
            row.style.paddingTop = 12;
            row.style.paddingLeft = 12;
            row.style.paddingRight = 12;
            row.style.marginBottom = 15;
            row.style.borderTopLeftRadius = 16;
            row.style.borderTopRightRadius = 16;
            row.style.borderBottomLeftRadius = 16;
            row.style.borderBottomRightRadius = 16;

            var title = new Label(controller.name);
            title.AddToClassList("controller-title");
            title.style.color = Color.white;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 16;
            title.style.marginBottom = 10;
            
            var toggleTrigger = new Toggle("Simuler Trigger");
            toggleTrigger.value = controller.SimulateTrigger;
            toggleTrigger.style.color = Color.white;
            toggleTrigger.style.marginBottom = 10;
            
            toggleTrigger.RegisterValueChangedCallback(evt => 
            {
                controller.SimulateTrigger = evt.newValue;
            });

            var buttons = new VisualElement();
            buttons.AddToClassList("controller-buttons");
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.SpaceBetween;

            Action actionA = () => controller.HandleAPressed();
            var btnA = new Button(actionA)
            {
                text = "A",
                userData = actionA
            };
            btnA.AddToClassList("btn-action-blue");
            btnA.style.width = Length.Percent(48);
            btnA.style.height = 45;
            btnA.style.fontSize = 18;

            Action actionThumb = () => 
            {
                if (controller is DefaultController pc)
                {
                    //pc.ForceCenterCursor();
                }
                controller.HandleThumb();
            };
            var btnThumb = new Button(actionThumb)
            {
                text = "Thumb",
                userData = actionThumb
            };
            btnThumb.AddToClassList("btn-action-green");
            btnThumb.style.width = Length.Percent(48);
            btnThumb.style.height = 45;
            btnThumb.style.fontSize = 18;

            buttons.Add(btnA);
            buttons.Add(btnThumb);

            row.Add(title);
            row.Add(toggleTrigger);
            row.Add(buttons);

            // --- Section "Test Raccourcis" ---
            // Lit les combos assignés au ControllerShortcutHandler de ce controller
            // et crée un bouton par combo pour permettre le test en simultaneé
            var shortcutHandler = (controller as MonoBehaviour)?.GetComponent<ControllerShortcutHandler>();
            if (shortcutHandler != null)
            {
                var assignedShortcuts = shortcutHandler.GetAllAssignedShortcuts()
                    .Where(kvp => kvp.Value != null && kvp.Value.Count == 2)
                    .ToList();

                if (assignedShortcuts.Count > 0)
                {
                    // Titre de la section
                    var shortcutTitle = new Label("🎮 Test Raccourcis");
                    shortcutTitle.style.color = new Color(0.6f, 1f, 0.6f);
                    shortcutTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                    shortcutTitle.style.fontSize = 13;
                    shortcutTitle.style.marginTop = 10;
                    shortcutTitle.style.marginBottom = 6;
                    row.Add(shortcutTitle);

                    foreach (var kvp in assignedShortcuts)
                    {
                        MethodInfo capturedMethod = kvp.Key;
                        string evt1 = kvp.Value[0];
                        string evt2 = kvp.Value[1];

                        // Attribut [ShortCut] pour le nom d'affichage
                        var attr = capturedMethod.GetCustomAttribute<ShortCutAttribute>();
                        string displayName = attr?.DisplayName ?? capturedMethod.Name;
                        string btnLabel = $"▶ {displayName}  [{evt1} + {evt2}]";

                        var capturedHandler = shortcutHandler;
                        Action testAction = () =>
                        {
                            Debug.Log($"[Debugger] 🎮 Test : {displayName} ({evt1} + {evt2})");
                            capturedHandler.SimulateCombo(evt1, evt2);
                        };

                        var btnTest = new Button(testAction)
                        {
                            text = btnLabel,
                            userData = testAction
                        };
                        btnTest.style.height = 36;
                        btnTest.style.fontSize = 12;
                        btnTest.style.marginBottom = 4;
                        btnTest.style.backgroundColor = new Color(0.12f, 0.35f, 0.18f, 0.95f);
                        btnTest.style.color = Color.white;
                        btnTest.style.whiteSpace = WhiteSpace.Normal;
                        row.Add(btnTest);
                    }
                }
                else
                {
                    // Aucun raccourci assigné : message d'aide
                    var noShortcutLabel = new Label("🎮 Aucun raccourci assigné");
                    noShortcutLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                    noShortcutLabel.style.fontSize = 12;
                    noShortcutLabel.style.marginTop = 8;
                    row.Add(noShortcutLabel);
                }
            }

            _controllersContainer.Add(row);
        }
    }

    private void AddStartOverCalibrationButton(VisualElement parent)
    {
        Action startOverCalibrationAction = () =>
        {
            if (CalibrationManager.instance == null)
            {
                Debug.LogWarning("[UIInputToolDebugger] Impossible de recommencer la calibration : CalibrationManager.instance est null.");
                return;
            }

            CalibrationManager.instance.StartOverCalibration();
        };

        var startOverButton = new Button(startOverCalibrationAction)
        {
            text = "Recommencer la calibration",
            userData = startOverCalibrationAction
        };

        startOverButton.style.height = 45;
        startOverButton.style.fontSize = 16;
        startOverButton.style.marginBottom = 15;
        startOverButton.style.backgroundColor = new Color(0.65f, 0.18f, 0.18f, 0.95f);
        startOverButton.style.color = Color.white;
        startOverButton.style.unityFontStyleAndWeight = FontStyle.Bold;

        parent.Add(startOverButton);
    }

    public bool TryInteractWithDebugger(Vector2 screenPos, bool isClick)
    {
        if (_root == null || _root.panel == null)
        {
            //if (isClick) Debug.Log("[UIInputToolDebugger] TryInteractWithDebugger: _root ou _root.panel est NULL.");
            return false;
        }
        
        Vector2 screenPosTopLeft = new Vector2(screenPos.x, Screen.height - screenPos.y);
        
        Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(_root.panel, screenPosTopLeft);
        
        _pickedElements.Clear();
        _root.panel.PickAll(panelPos, _pickedElements);

        if (isClick)
        {
            //Debug.Log($"[UIInputToolDebugger] Click! UnityScreen: {screenPos} -> UIPanel: {panelPos}");
            //Debug.Log($"[UIInputToolDebugger] Elements Picked: {_pickedElements.Count}");
        }

        if (_pickedElements.Count == 0) return false;

        bool isInsideDebugger = false;

        foreach (var elem in _pickedElements)
        {
            if (elem == _root || elem.name == "root" || elem.name == "controllers-scroll" || elem.name == "controllers-container")
            {
                isInsideDebugger = true;
                break;
            }
        }

        if (!isInsideDebugger)
        {
            //if (isClick) Debug.Log($"[UIInputToolDebugger] Clic hors du debugger.");
            return false;
        }

        if (isClick)
        {
            foreach (var elem in _pickedElements)
            {
                if (elem is Button btn && btn.userData is Action action)
                {
                    //Debug.Log($"[UIInputToolDebugger] Clic intercepté via PickAll sur le bouton '{btn.text}'. Exécution directe !");
                    action.Invoke();
                    return true; // Clic validé et absorbé
                }
            }
            //Debug.Log($"[UIInputToolDebugger] Clic à l'intérieur du debugger, mais aucun bouton n'a été trouvé sous le curseur.");
        }

        return true; 
    }
}