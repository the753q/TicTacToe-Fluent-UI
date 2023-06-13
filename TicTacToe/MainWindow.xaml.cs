using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Xml.Linq;
using TicTacToe;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Security.Cryptography.Core;
using Windows.UI.ViewManagement;
using WinRT;
using static System.Net.Mime.MediaTypeNames;


class WindowsSystemDispatcherQueueHelper
{
    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions
    {
        internal int dwSize;
        internal int threadType;
        internal int apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController([In] DispatcherQueueOptions options, [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object dispatcherQueueController);

    object m_dispatcherQueueController = null;
    public void EnsureWindowsSystemDispatcherQueueController()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null)
        {
            // one already exists, so we'll just use it.
            return;
        }

        if (m_dispatcherQueueController == null)
        {
            DispatcherQueueOptions options;
            options.dwSize = Marshal.SizeOf(typeof(DispatcherQueueOptions));
            options.threadType = 2;    // DQTYPE_THREAD_CURRENT
            options.apartmentType = 2; // DQTAT_COM_STA

            CreateDispatcherQueueController(options, ref m_dispatcherQueueController);
        }
    }
}


namespace TicTacToe
{
    public sealed partial class MainWindow : Window
    {
        WindowsSystemDispatcherQueueHelper m_wsdqHelper;
        MicaController m_backdropController;
        SystemBackdropConfiguration m_configurationSource;

        public MainWindow()
        {
            this.InitializeComponent();
            TrySetSystemBackdrop();
            InitializeWindow();
            InitializeGame();
        }

        bool TrySetSystemBackdrop()
        {
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                m_wsdqHelper = new WindowsSystemDispatcherQueueHelper();
                m_wsdqHelper.EnsureWindowsSystemDispatcherQueueController();

                // Create the policy object.
                m_configurationSource = new SystemBackdropConfiguration();
                this.Activated += Window_Activated;
                this.Closed += Window_Closed;
                ((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;

                // Initial configuration state.
                m_configurationSource.IsInputActive = true;
                SetConfigurationSourceTheme();

                m_backdropController = new Microsoft.UI.Composition.SystemBackdrops.MicaController();

                // Enable the system backdrop.
                // Note: Be sure to have "using WinRT;" to support the Window.As<...>() call.
                m_backdropController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                m_backdropController.SetSystemBackdropConfiguration(m_configurationSource);
                return true; // succeeded
            }
            return false; // Mica is not supported on this system
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            m_configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            // Make sure any Mica/Acrylic controller is disposed
            // so it doesn't try to use this closed window.
            if (m_backdropController != null)
            {
                m_backdropController.Dispose();
                m_backdropController = null;
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
                case ElementTheme.Dark: m_configurationSource.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark; break;
                case ElementTheme.Light: m_configurationSource.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light; break;
                case ElementTheme.Default: m_configurationSource.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Default; break;
            }
        }

        private void InitializeWindow()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(500, 500, 254, 380));
            appWindow.Title = "Tic Tac Toe";
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            OverlappedPresenter overlappedPresenter = OverlappedPresenter.Create();
            overlappedPresenter.IsResizable = false;
            overlappedPresenter.IsMaximizable = false;
            appWindow.SetPresenter(overlappedPresenter);
            appWindow.SetIcon("Assets/favicon.ico");
        }

        private int nextPlayer = 0;
        private string bot = "None";
        private int[] gameBoard = {0,0,0,0,0,0,0,0,0};

        private void InitializeGame()
        {
            ContentGrid.Visibility = Visibility.Collapsed;
            PlayerSelectionGrid.Visibility = Visibility.Visible;
            this.nextPlayer = 0;
            this.bot = "None";
            for(int i=0;i<gameBoard.Length;i++)
            {
                this.gameBoard[i] = 0;
                btn0.Content = "";
                btn1.Content = "";
                btn2.Content = "";
                btn3.Content = "";
                btn4.Content = "";
                btn5.Content = "";
                btn6.Content = "";
                btn7.Content = "";
                btn8.Content = "";
            }
        }

        private void StartGame()
        { 
            PlayerSelectionGrid.Visibility = Visibility.Collapsed;
            ContentGrid.Visibility = Visibility.Visible;
            StatusText.Text = "Choose Bot!";
            BotSelector.SelectedItem = null;
            NextTurn();
        }

        private void NextTurn()
        {
            if(this.bot == "None") { return; }
            if(this.nextPlayer == 1)
            {
                StatusText.Text = "Your Turn!";
            }
            else if (this.nextPlayer == 2)
            {
                StatusText.Text = "Bot's Turn";
                StatusRing.IsActive = true;
            }
        }

        private void RedClick(object sender, RoutedEventArgs e)
        {
            this.nextPlayer = 1;
            StartGame();
        }
        private void GreenClick(object sender, RoutedEventArgs e)
        {
            this.nextPlayer = 2;
            StartGame();
        }
        private void Bot_Changed(object sender, RoutedEventArgs e)
        {
            ComboBox comboBox = (ComboBox)sender;
            if (comboBox.SelectedItem == null) { return; }
            string selectedName = comboBox.SelectedItem.ToString();
            TEMPtext.Text = selectedName;
            if(selectedName == this.bot) { return; }
            if(this.bot == "None")
            {
                this.bot = selectedName;
                NextTurn();
                return;
            }
            InitializeGame();
        }



        private void TileClick(object sender, RoutedEventArgs e)
        {
            if(bot == "None") { return; }
            Button tile = (Button) sender;
            var btnName = tile.Name;
            TEMPtext.Text = btnName;
            if (UpdateBoard(btnName))
            {
                if (this.nextPlayer == 1)
                {
                    tile.Content = "❌";
                }
                else
                {
                    tile.Content = "🟢";
                }
            }
        }

        private bool UpdateBoard(string btnName)
        {
            int btnNumber = btnName[3] - '0';
            if (btnNumber != 10 && gameBoard[btnNumber] == 0)
            {
                gameBoard[btnNumber] = nextPlayer;
                return true;
            }
            return false;

        }
    }
}