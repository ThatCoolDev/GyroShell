﻿using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using System;
using AppWindow = Microsoft.UI.Windowing.AppWindow;
using Windows.Graphics;
using GyroShell.Helpers;
using Microsoft.UI.Composition.SystemBackdrops;
using WinRT;
using Windows.UI;
using Microsoft.UI.Xaml.Media.Animation;
using System.Threading;
using static GyroShell.Helpers.Win32.Win32Interop;
using static GyroShell.Helpers.Win32.WindowMessage;
using static GyroShell.Helpers.Win32.GetWindowName;
using static GyroShell.Helpers.TaskbarManager;
using static GyroShell.Helpers.Win32.ScreenValues;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Documents;
using System.Collections.Generic;
using Windows.Media.Capture;
using System.Windows.Automation;

namespace GyroShell
{
    public sealed partial class MainWindow : Window
    {
        AppWindow m_AppWindow;

        private IntPtr _oldWndProc;
        internal static IntPtr hWnd;

        internal static int uCallBack;

        internal static bool fBarRegistered = false;
        private bool finalOpt;

        private byte finalA;
        private byte finalR;
        private byte finalG;
        private byte finalB;

        private float finalLO;
        private float finalTO;

        private static string name;

        public MainWindow()
        {
            this.InitializeComponent();
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            TaskbarManager.Init();

            // Presenter handling code
            var presenter = GetAppWindowAndPresenter();
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            m_AppWindow = GetAppWindowForCurrentWindow();
            m_AppWindow.SetPresenter(AppWindowPresenterKind.Default);

            // Resize Window
            hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (GyroShell.Helpers.OSVersion.IsWin11())
            {
                var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
                var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hWnd, attribute, ref preference, sizeof(uint));
            }

            // Hide in ALT+TAB view
            int exStyle = (int)GetWindowLongPtr(hWnd, -20);
            exStyle |= 128;
            SetWindowLongPtr(hWnd, -20, (IntPtr)exStyle);

            Thread.Sleep(20); //TODO: Stop the window message from moving our window into the wokring area

            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);
            int barHeight = 48;

            Title = "GyroShell";
            appWindow.Resize(new SizeInt32 { Width = screenWidth, Height = barHeight });
            appWindow.Move(new PointInt32 { X = 0, Y = screenHeight - barHeight });
            appWindow.MoveInZOrderAtTop();

            // Init stuff
            RegisterBar();
            //RegisterWinEventHook();
            _oldWndProc = SetWndProc(WindowProcess);
            MonitorSummon();
            TaskbarFrame.Navigate(typeof(Controls.DefaultTaskbar), null, new SuppressNavigationTransitionInfo());
            SetBackdrop();

            // Show GyroShell when everything is ready
            m_AppWindow.Show();
        }

        #region Window Handling
        private void OnProcessExit(object sender, EventArgs e)
        {
            TaskbarManager.ShowTaskbar();
        }

        private OverlappedPresenter GetAppWindowAndPresenter()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId WndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var _apw = AppWindow.GetFromWindowId(WndId);

            return _apw.Presenter as OverlappedPresenter;
        }
        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWndApp = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId WndIdApp = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWndApp);

            return AppWindow.GetFromWindowId(WndIdApp);
        }

        public void MonitorSummon()
        {

            bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect lprcMonitor, IntPtr dwData)
            {
                return true;
            }

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);
        }
        #endregion

        #region Backdrop Stuff
        WindowsSystemDispatcherQueueHelper m_wsdqHelper;
        MicaController micaController;
        DesktopAcrylicController acrylicController;
        SystemBackdropConfiguration m_configurationSource;

        private void SetBackdrop()
        {
            bool? option = App.localSettings.Values["isCustomTransparency"] as bool?;
            byte? alpha = App.localSettings.Values["aTint"] as byte?;
            byte? red = App.localSettings.Values["rTint"] as byte?;
            byte? green = App.localSettings.Values["gTint"] as byte?;
            byte? blue = App.localSettings.Values["bTint"] as byte?;
            float? luminOpacity = App.localSettings.Values["luminOpacity"] as float?;
            float? tintOpacity = App.localSettings.Values["tintOpacity"] as float?;
            finalOpt = option != null ? (bool)option : false;
            finalA = alpha != null ? (byte)alpha : (byte)0;
            finalR = red != null ? (byte)red : (byte)0;
            finalG = green != null ? (byte)green : (byte)0;
            finalB = blue != null ? (byte)blue : (byte)0;
            finalLO = luminOpacity != null ? (float)luminOpacity : (float)0.2f;
            finalTO = tintOpacity != null ? (float)tintOpacity : (float)0.3f;

            int? transparencyType = App.localSettings.Values["transparencyType"] as int?;
            switch (transparencyType)
            {
                case 0:
                default:
                    if (Helpers.OSVersion.IsWin11())
                    {
                        TrySetMicaBackdrop(MicaKind.BaseAlt);
                    }
                    else
                    {
                        TrySetAcrylicBackdrop();
                    }
                    break;
                case 1:
                    if (Helpers.OSVersion.IsWin11())
                    {
                        TrySetMicaBackdrop(MicaKind.Base);
                    }
                    else
                    {
                        TrySetAcrylicBackdrop();
                    }
                    break;
                case 2:
                    TrySetAcrylicBackdrop();
                    break;
            }
        }

        bool TrySetMicaBackdrop(MicaKind micaKind)
        {
            if (MicaController.IsSupported())
            {
                m_wsdqHelper = new WindowsSystemDispatcherQueueHelper();
                m_wsdqHelper.EnsureWindowsSystemDispatcherQueueController();
                m_configurationSource = new Microsoft.UI.Composition.SystemBackdrops.SystemBackdropConfiguration();
                this.Activated += Window_Activated;
                this.Closed += Window_Closed;
                ((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;
                m_configurationSource.IsInputActive = true;
                SetConfigurationSourceTheme();
                micaController = new Microsoft.UI.Composition.SystemBackdrops.MicaController();
                micaController.Kind = micaKind;
                if (finalOpt == true)
                {
                    micaController.TintColor = Color.FromArgb(finalA, finalR, finalG, finalB);
                    micaController.TintOpacity = finalTO;
                }
                micaController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                micaController.SetSystemBackdropConfiguration(m_configurationSource);
                return true;
            }
            TrySetAcrylicBackdrop();
            return false;
        }
        bool TrySetAcrylicBackdrop()
        {
            if (DesktopAcrylicController.IsSupported())
            {
                m_wsdqHelper = new WindowsSystemDispatcherQueueHelper();
                m_wsdqHelper.EnsureWindowsSystemDispatcherQueueController();
                m_configurationSource = new SystemBackdropConfiguration();
                this.Activated += Window_Activated;
                this.Closed += Window_Closed;
                m_configurationSource.IsInputActive = true;
                SetConfigurationSourceTheme();
                acrylicController = new DesktopAcrylicController();
                acrylicController.TintColor = Color.FromArgb(finalA, finalR, finalG, finalB);
                acrylicController.TintOpacity = finalTO;
                acrylicController.LuminosityOpacity = finalLO;
                ((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;
                acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                acrylicController.SetSystemBackdropConfiguration(m_configurationSource);
                return true;
            }
            return false;
        }

        private void Window_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
        {
            m_configurationSource.IsInputActive = true;
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            RegisterBar();
            if (micaController != null)
            {
                micaController.Dispose();
                micaController = null;
            }
            if (acrylicController != null)
            {
                acrylicController.Dispose();
                acrylicController = null;
            }
            this.Activated -= Window_Activated;
            m_configurationSource = null;
        }

        private void Window_ThemeChanged(FrameworkElement sender, object args)
        {
            if (m_configurationSource != null)
            {
                SetConfigurationSourceTheme();
            }
        }

        private void SetConfigurationSourceTheme()
        {
            switch (((FrameworkElement)this.Content).ActualTheme)
            {
                case ElementTheme.Dark: m_configurationSource.Theme = SystemBackdropTheme.Dark; if (acrylicController != null) { acrylicController.TintColor = Color.FromArgb(255, 0, 0, 0); } break;
                case ElementTheme.Light: m_configurationSource.Theme = SystemBackdropTheme.Light; if (acrylicController != null) { acrylicController.TintColor = Color.FromArgb(255, 255, 255, 255); } break;
                case ElementTheme.Default: m_configurationSource.Theme = SystemBackdropTheme.Default; if (acrylicController != null) { acrylicController.TintColor = Color.FromArgb(255, 0, 0, 0); } break;
            }
        }
        #endregion

        #region AppBar
        internal static void RegisterBar()
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = hWnd;
            if (!fBarRegistered)
            {
                uCallBack = RegisterWindowMessage("AppBarMessage");
                abd.uCallbackMessage = uCallBack;

                uint ret = SHAppBarMessage((int)ABMsg.ABM_NEW, ref abd);
                bool regShellHook = RegisterShellHookWindow(hWnd);
                fBarRegistered = true;

                AutoHideExplorer(true);
                ABSetPos();
                AutoHideExplorer(false);
                HideTaskbar();
                SetWindowPos(hWnd, (IntPtr)WindowZOrder.HWND_TOPMOST, 0, 0, 0, 0, (int)SWPFlags.SWP_NOMOVE | (int)SWPFlags.SWP_NOSIZE | (int)SWPFlags.SWP_SHOWWINDOW);
            }
            else
            {
                SHAppBarMessage((int)ABMsg.ABM_REMOVE, ref abd);
                bool deRegShellHook = DeregisterShellHookWindow(hWnd);
                fBarRegistered = false;
            }
        }

        private static void ABSetPos()
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = hWnd;
            abd.uEdge = (int)ABEdge.ABE_BOTTOM;

            abd.rc.left = 0;
            abd.rc.right = GetScreenWidth();
            if (abd.uEdge == (int)ABEdge.ABE_TOP)
            {
                abd.rc.top = 0;
                abd.rc.bottom = 48;
            }
            else
            {
                abd.rc.bottom = GetScreenHeight();
                abd.rc.top = abd.rc.bottom - 46;
            }

            SHAppBarMessage((int)ABMsg.ABM_QUERYPOS, ref abd);

            switch (abd.uEdge)
            {
                case (int)ABEdge.ABE_LEFT:
                    abd.rc.right = abd.rc.left + 48;
                    break;
                case (int)ABEdge.ABE_RIGHT:
                    abd.rc.left = abd.rc.right - 48;
                    break;
                case (int)ABEdge.ABE_TOP:
                    abd.rc.bottom = abd.rc.top + 48;
                    break;
                case (int)ABEdge.ABE_BOTTOM:
                    abd.rc.top = abd.rc.bottom - 48;
                    break;
            }

            SHAppBarMessage((int)ABMsg.ABM_SETPOS, ref abd);
            MoveWindow(abd.hWnd, abd.rc.left, abd.rc.top, abd.rc.right - abd.rc.left, abd.rc.bottom - abd.rc.top, true);
        }
#endregion

        #region Callbacks

        #region SetWinEventHook Init
        private static readonly WinEventDelegate callback = WinEventCallback;
        private static void RegisterWinEventHook()
        {
            SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }
        #endregion

        // SetWinEventHook Callback
        internal static void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            name = (GetWindowTitle(hwnd));
            if (name.Length > 0)
            {
                Debug.WriteLine(hwnd);
                Debug.WriteLine(eventType);
   
                Debug.WriteLine(name);
                Debug.WriteLine("--------------");
            }
        }

        #region WndProc Init
        private static WndProcDelegate _currDelegate = null;
        public static IntPtr SetWndProc(WndProcDelegate newProc)
        {
            _currDelegate = newProc;

            IntPtr newWndProcPtr = Marshal.GetFunctionPointerForDelegate(newProc);

            if (IntPtr.Size == 8)
            {
                return SetWindowLongPtr(hWnd, GWLP_WNDPROC, newWndProcPtr);
            }
            else
            {
                return SetWindowLong(hWnd, GWLP_WNDPROC, newWndProcPtr);
            }
        }
        #endregion

        // WNDPROC Callback
        private IntPtr WindowProcess(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            /*Debug.WriteLine("------------");
            Debug.WriteLine("MESSAGE: " + (WM_CODE)message);
            Debug.WriteLine(wParam);
            Debug.WriteLine(lParam);*/
            

            return CallWindowProc(_oldWndProc, hwnd, message, wParam, lParam);
        }

        #endregion
    }
}