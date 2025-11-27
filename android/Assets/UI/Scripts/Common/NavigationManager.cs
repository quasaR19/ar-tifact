using System.Collections.Generic;
using ARArtifact.Services;
using UnityEngine;

namespace ARArtifact.UI.Common
{
    public class NavigationManager : MonoBehaviour
    {
        public static NavigationManager Instance { get; private set; }

        private Stack<BaseScreenController> _navigationStack = new Stack<BaseScreenController>();
        
        [SerializeField] private BaseScreenController _homeScreen;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void NavigateTo(BaseScreenController screen)
        {
            if (screen == null)
            {
                Debug.LogError("[NavigationManager] NavigateTo: screen is null!");
                return;
            }

            Debug.Log($"[NavigationManager] NavigateTo: {screen.GetType().Name}, gameObject.activeSelf={screen.gameObject.activeSelf}");

            // Hide current screen if exists
            if (_navigationStack.Count > 0)
            {
                var current = _navigationStack.Peek();
                if (current != null && current != screen)
                {
                    Debug.Log($"[NavigationManager] Скрываем текущий экран: {current.GetType().Name}");
                    current.Hide();
                }
            }

            // НЕ трогаем gameObject.SetActive - это ломает панель UIDocument!
            // Все экраны остаются активными, скрытие/показ через DisplayStyle

            _navigationStack.Push(screen);
            Debug.Log($"[NavigationManager] Вызываем Show() для {screen.GetType().Name}");
            screen.Show();
            
            // Subscribe to close event if not already
            // Note: Be careful about multiple subscriptions. 
            // BaseScreenController implementation should handle cleanup or we do it here.
            screen.OnClose -= OnScreenClosed; // Unsubscribe to avoid duplicates
            screen.OnClose += OnScreenClosed;
            
            Debug.Log($"[NavigationManager] NavigateTo завершен для {screen.GetType().Name}, gameObject.activeSelf={screen.gameObject.activeSelf}");
            
            // Управление камерой: отключаем камеру на всех экранах, кроме MainScreen
            UpdateCameraState(screen);
        }

        public void GoBack()
        {
            if (_navigationStack.Count > 0)
            {
                var current = _navigationStack.Pop();
                if (current != null)
                {
                    current.Hide();
                    current.OnClose -= OnScreenClosed;
                }

                if (_navigationStack.Count > 0)
                {
                    var previous = _navigationStack.Peek();
                    if (previous != null)
                    {
                        previous.Show();
                        
                        // Управление камерой: проверяем, нужно ли включить камеру
                        UpdateCameraState(previous);
                    }
                }
                else if (_homeScreen != null)
                {
                    // Fallback to home screen if stack empty
                    _homeScreen.Show();
                    _navigationStack.Push(_homeScreen);
                    
                    // Управление камерой: проверяем, нужно ли включить камеру
                    UpdateCameraState(_homeScreen);
                }
            }
        }

        private void OnScreenClosed()
        {
            GoBack();
        }

        public void SetHomeScreen(BaseScreenController homeScreen)
        {
            _homeScreen = homeScreen;
            if (_navigationStack.Count == 0)
            {
                _navigationStack.Push(_homeScreen);
                _homeScreen.Show();
                
                // Управление камерой: проверяем, нужно ли включить камеру
                UpdateCameraState(_homeScreen);
            }
        }
        
        /// <summary>
        /// Обновляет состояние AR камеры в зависимости от активного экрана
        /// Камера работает только на MainScreen
        /// </summary>
        private void UpdateCameraState(BaseScreenController screen)
        {
            if (screen == null) return;
            
            var arManager = ARManager.Instance;
            if (arManager == null) return;
            
            // Проверяем, является ли текущий экран MainScreen
            // Используем проверку по типу компонента
            bool isMainScreen = screen is ARArtifact.UI.MainScreenController;
            
            if (isMainScreen)
            {
                Debug.Log("[NavigationManager] Переход на MainScreen - включаем камеру");
                arManager.EnableCamera();
            }
            else
            {
                Debug.Log($"[NavigationManager] Переход на {screen.GetType().Name} - отключаем камеру");
                arManager.DisableCamera();
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Сбрасывает стек навигации (используется для горячей перезагрузки UI в редакторе)
        /// </summary>
        public void ResetStack()
        {
            while (_navigationStack.Count > 0)
            {
                var screen = _navigationStack.Pop();
                if (screen != null)
                {
                    screen.OnClose -= OnScreenClosed;
                    screen.Hide();
                }
            }
        }
#endif
    }
}

