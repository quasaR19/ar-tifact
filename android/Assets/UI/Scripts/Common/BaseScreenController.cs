using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARArtifact.UI.Common
{
    public abstract class BaseScreenController : MonoBehaviour
    {
        protected UIDocument _uiDocument;
        protected VisualElement _root;
        
        // Header elements
        protected VisualElement _header;
        protected Label _screenTitle;
        protected Button _closeButton;
        protected VisualElement _headerRightContainer;

        public event Action OnClose;

        public virtual void Initialize(UIDocument uiDocument, string screenName = "")
        {
            Debug.Log($"[BaseScreenController] Initialize вызван для {GetType().Name}, uiDocument={uiDocument != null}");
            
            // НЕ трогаем gameObject.SetActive - это ломает панель UIDocument!
            
            _uiDocument = uiDocument;
            
            // Ждем, пока rootVisualElement станет доступен
            if (_uiDocument != null)
            {
                _root = _uiDocument.rootVisualElement;
                if (_root == null)
                {
                    Debug.LogWarning($"[BaseScreenController] rootVisualElement is null для {GetType().Name}, пытаемся подождать...");
                    // Пытаемся получить root еще раз
                    _root = _uiDocument.rootVisualElement;
                }
            }
            
            Debug.Log($"[BaseScreenController] После получения root для {GetType().Name}, _root={(_root != null ? "found" : "null")}");
            
            if (_root == null)
            {
                Debug.LogError($"[BaseScreenController] _root is null для {GetType().Name}! Не могу инициализировать UI.");
                return;
            }
            
            // Setup Header
            _header = _root.Q<VisualElement>(className: "header");
            if (_header != null)
            {
                _screenTitle = _header.Q<Label>(className: "header__title");
                if (_screenTitle != null && !string.IsNullOrEmpty(screenName))
                {
                    _screenTitle.text = screenName;
                }

                _headerRightContainer = _header.Q<VisualElement>(className: "header__right");
                
                // Find close button by name or class, standardizing on name "close-button"
                _closeButton = _header.Q<Button>("close-button");
                if (_closeButton != null)
                {
                    _closeButton.clicked += OnCloseClicked;
                    Debug.Log($"[BaseScreenController] Кнопка закрытия найдена и подписана для {GetType().Name}");
                }
                else
                {
                    Debug.LogWarning($"[BaseScreenController] Кнопка закрытия не найдена для {GetType().Name}. Проверка элементов в header:");
                    if (_header != null)
                    {
                        var allButtons = _header.Query<Button>().ToList();
                        Debug.LogWarning($"[BaseScreenController] Найдено кнопок в header: {allButtons.Count}");
                        foreach (var btn in allButtons)
                        {
                            Debug.LogWarning($"[BaseScreenController] Кнопка: name={btn.name}, classList={string.Join(", ", btn.GetClasses())}");
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[BaseScreenController] Header не найден для {GetType().Name}");
            }
            
            OnInitialize();
            
            // Скрываем экран по умолчанию (кроме LaunchScreen)
            if (ShouldHideOnInit())
            {
                Hide();
            }
        }

        protected virtual void OnInitialize() { }
        
        /// <summary>
        /// Определяет, должен ли экран быть скрыт при инициализации
        /// По умолчанию true, LaunchScreen переопределяет как false
        /// </summary>
        protected virtual bool ShouldHideOnInit()
        {
            return true;
        }

        protected virtual void OnCloseClicked()
        {
            Debug.Log($"[BaseScreenController] OnCloseClicked вызван для {GetType().Name}");
            OnClose?.Invoke();
            Hide();
        }

        public virtual void Show()
        {
            Debug.Log($"[BaseScreenController] Show() вызван для {GetType().Name}, _root={(_root != null ? "found" : "null")}");
            
            // НЕ отключаем/включаем GameObject - это ломает панель UIDocument!
            // Только показываем/скрываем через DisplayStyle
            
            if (_root == null && _uiDocument != null)
            {
                _root = _uiDocument.rootVisualElement;
            }
            
            if (_root != null)
            {
                _root.style.display = DisplayStyle.Flex;
            }
        }

        public virtual void Hide()
        {
            // НЕ отключаем GameObject - это ломает панель UIDocument!
            // Только скрываем через DisplayStyle
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        public void SetTitle(string title)
        {
            if (_screenTitle != null)
            {
                _screenTitle.text = title;
            }
        }

        protected void AddHeaderButton(Button button)
        {
            if (_headerRightContainer != null)
            {
                // Add before close button if it exists
                if (_closeButton != null)
                {
                    _headerRightContainer.Insert(_headerRightContainer.IndexOf(_closeButton), button);
                }
                else
                {
                    _headerRightContainer.Add(button);
                }
            }
        }
    }
}

