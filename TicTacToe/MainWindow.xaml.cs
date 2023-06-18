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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Xml.Linq;
using TicTacToe;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Security.Cryptography.Core;
using Windows.UI.ViewManagement;
using WinRT;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;


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

            _ = CreateDispatcherQueueController(options, ref m_dispatcherQueueController);
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
            InitializeComponent();
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


        private string player = null;
        private string nextPlayer = null;
        private string bot = null;
        private string[] gameBoard = new string[9];

        private void InitializeGame()
        {
            ContentGrid.Visibility = Visibility.Collapsed;
            PlayerSelectionGrid.Visibility = Visibility.Visible;
            ResetBtn.Visibility = Visibility.Collapsed;
            player = null;
            nextPlayer = null;
            for (int i = 0; i < gameBoard.Length; i++)
            {
                gameBoard[i] = null;
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
            if (bot == null) { StatusText.Text = "Choose Bot!"; }
            else { NextTurn(); }
        }

        private void RedClick(object sender, RoutedEventArgs e)
        {
            player = "red";
            nextPlayer = "Player";
            StartGame();
        }

        private void GreenClick(object sender, RoutedEventArgs e)
        {
            player = "green";
            nextPlayer = "Bot";
            StartGame();
        }

        private string TestBoardWin()
        {
            for (int start = 0; start < 3; start++)
            {
                string color = gameBoard[start];
                if (color == null) { continue; }
                if (start == 0)
                {
                    if (gameBoard[3] == color && gameBoard[6] == color) { return color; }
                    if (gameBoard[4] == color && gameBoard[8] == color) { return color; }
                    if (gameBoard[1] == color && gameBoard[2] == color) { return color; }
                }
                else if (start == 1)
                {
                    if (gameBoard[4] == color && gameBoard[7] == color) { return color; }
                }
                else
                {
                    if (gameBoard[4] == color && gameBoard[6] == color) { return color; }
                    if (gameBoard[5] == color && gameBoard[8] == color) { return color; }
                }
            }
            string color2 = gameBoard[6];
            if (gameBoard[7] == color2 && gameBoard[8] == color2) { return color2; }
            return null;
        }

        private bool IsGameWon()
        {
            string winner = TestBoardWin();
            if (winner != null)
            {
                // END GAME
                TEMPtext.Text = winner + " won!";
                StatusText.Text = winner + " won!";
                return true;
            }
            return false;
        }

        private bool IsGameOver()
        {
            bool isWon = false;
            if (IsGameWon()) { isWon = true; }
            if (!isWon)
            {
                bool boardFilled = true;
                for (int i = 0; i < 9; i++)
                {
                    if (gameBoard[i] == null)
                    {
                        boardFilled = false;
                        break;
                    }
                }
                if (boardFilled)
                {
                    TEMPtext.Text = "Tie!";
                    StatusText.Text = "It's a tie!";
                    isWon = true;
                }
            }
            if (isWon) 
            {
                ResetBtn.Visibility = Visibility.Visible;
                return true;
            }
            return false;
        }


        private void RandomBot()
        {
            var rand = new Random();
            bool moveSuccessful = false;
            while (!moveSuccessful)
            {
                moveSuccessful = UpdateBoard(rand.Next(9));
            }
        }

        private void ProBot()
        {
            _ = UpdateBoard(4);
        }

        private void NextTurn()
        {
            if (bot == null || IsGameOver()) { return; }
            if (nextPlayer == "Player")
            {
                StatusText.Text = "Your Turn!";
            }
            else if (nextPlayer == "Bot")
            {
                StatusText.Text = "Bot's Turn";
                TEMPtext.Text = bot;
                if (bot == "Random Bot") { RandomBot(); }
                else { ProBot(); }
                nextPlayer = "Player";
                NextTurn();
            }
        }


        private void TileClick(object sender, RoutedEventArgs e)
        {
            if (bot == null || nextPlayer == "Bot") { return; }
            Button tile = (Button)sender;
            var btnName = tile.Name;
            int btnNumber = btnName[3] - '0';
            if (UpdateBoard(btnNumber))
            {
                nextPlayer = "Bot";
                NextTurn();
            }
        }

        private bool UpdateBoard(int btnNumber)
        {
            if (gameBoard[btnNumber] == null && !IsGameOver())
            {
                gameBoard[btnNumber] = nextPlayer;
                string btnName = "btn" + btnNumber.ToString();
                Button button = (Button)ButtonGrid.FindName(btnName);
                if (button == null)
                {
                    Console.WriteLine("BUTTON NULL!");
                    TEMPtext.Text = btnName + "Was null";
                    return false;
                }
                if ((player == "red" && nextPlayer == "Player") || (player == "green" && nextPlayer == "Bot"))
                {
                    button.Content = "❌";
                }
                else
                {
                    button.Content = "🟢";
                }
                return true;
            }
            return false;
        }

        private void Bot_Changed(object sender, RoutedEventArgs e)
        {
            ComboBox comboBox = (ComboBox)sender;
            if (comboBox.SelectedItem == null) { return; }
            string selectedName = comboBox.SelectedItem.ToString();
            TEMPtext.Text = selectedName;
            if (selectedName == bot) { return; }
            if (bot == null)
            {
                bot = selectedName;
                NextTurn();
                return;
            }
            bot = selectedName;
            InitializeGame();
        }

        private void ResetClick(object sender, RoutedEventArgs e)
        {
            InitializeGame();
        }
    }
}