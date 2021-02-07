﻿using ShutdownTimer.Helpers;
using System;
using System.Windows.Forms;

namespace ShutdownTimer
{
    public partial class Menu : Form
    {
        private readonly string[] startupArgs;
        private string checkResult;
        private string password; // used for password protection

        public Menu(string[] args)
        {
            InitializeComponent();
            startupArgs = args;
        }

        #region "form events"

        private void Menu_Load(object sender, EventArgs e)
        {
            ExceptionHandler.LogEvent("[Menu] Load menu");

            versionLabel.Text = "v" + Application.ProductVersion.Remove(Application.ProductVersion.LastIndexOf(".")); // Display current version
            infoToolTip.SetToolTip(gracefulCheckBox, "Applications that do not exit when prompted automatically get terminated by default to ensure a successful shutdown." +
                "\n\nA graceful shutdown on the other hand will wait for all applications to exit before continuing with the shutdown." +
                "\nThis might result in an unsuccessful shutdown if one or more applications are unresponsive or require a user interaction to exit!");
            infoToolTip.SetToolTip(preventSleepCheckBox, "Depending on the power settings of your system, it might go to sleep after certain amount of time due to inactivity." +
                "\nThis option will keep the system awake to ensure the timer can properly run and execute a shutdown.");
            infoToolTip.SetToolTip(backgroundCheckBox, "This will launch the countdown without a visible window but will show a tray icon in your taskbar.");
            infoToolTip.SetToolTip(hoursNumericUpDown, "This defines the hours to count down from. Use can use any positive whole number.");
            infoToolTip.SetToolTip(minutesNumericUpDown, "This defines the minutes to count down from. Use can use any positive whole number.\nValues above 59 will get converted into their corresponding seconds, minutes and hours.");
            infoToolTip.SetToolTip(secondsNumericUpDown, "This defines the seconds to count down from. Use can use any positive whole number.\nValues above 59 will get converted into their corresponding seconds, minutes and hours.");
        }

        private void Menu_Shown(object sender, EventArgs e)
        {
            // Check for startup arguments
            if (startupArgs.Length > 0) { ProcessArgs(); }
            else
            {
                // Load settings
                Application.DoEvents();
                LoadSettings();
            }
        }

        private void Menu_FormClosing(object sender, FormClosingEventArgs e)
        {
            SettingsProvider.Save();
        }

        private void ActionComboBox_TextChanged(object sender, EventArgs e)
        {
            // disables graceful checkbox for all modes which can not be executed gracefully / which always execute gracefully
            if (actionComboBox.Text == "Shutdown" || actionComboBox.Text == "Restart" || actionComboBox.Text == "Logout") { gracefulCheckBox.Enabled = true; }
            else { gracefulCheckBox.Enabled = false; }
        }

        private void SettingsButton_Click(object sender, EventArgs e)
        {
            Settings settings = new Settings();
            settings.ShowDialog();
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            ExceptionHandler.LogEvent("[Menu] Prepare start countdown");

            if (RunChecks())
            {
                // Disable controls
                startButton.Enabled = false;
                actionGroupBox.Enabled = false;
                timeGroupBox.Enabled = false;

                SaveSettings();

                if (SettingsProvider.Settings.PasswordProtection)
                {
                    ExceptionHandler.LogEvent("[Menu] Enabeling password protection");
                    using (var form = new InputBox())
                    {
                        form.Title = "Password Protection";
                        form.Message = "Please set a password to enable password protection.\n\n" +
                            "You can disable this dialog in the settings under Advanced > Password Protection";
                        form.PasswordMode = true;
                        var result = form.ShowDialog();
                        ExceptionHandler.LogEvent("[Menu] Saving password");
                        password = form.ReturnValue;
                    }
                }

                this.Hide();
                StartCountdown();
            }
            else
            {
                ExceptionHandler.LogEvent("[Menu] Invalid countdown");
                MessageBox.Show("The following error(s) occurred:\n\n" + checkResult + "Please try to resolve the(se) problem(s) and try again.", "There seems to be a problem!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        /// <summary>
        /// Checks user input before further processing
        /// </summary>
        /// <returns>Result of checks</returns>
        private bool RunChecks()
        {
            ExceptionHandler.LogEvent("[Menu] Run checks");

            bool errTracker = true; // if anything goes wrong the tracker will be set to false
            string errMessage = null; // error messages will append to this

            // Check if chosen action is a valid option
            if (!actionComboBox.Items.Contains(actionComboBox.Text))
            {
                errTracker = false;
                errMessage += "Please select a valid action from the dropdown menu!\n\n";
            }

            // Check if all time values are zero
            if (hoursNumericUpDown.Value == 0 && minutesNumericUpDown.Value == 0 && secondsNumericUpDown.Value == 0)
            {
                errTracker = false;
                errMessage += "The timer cannot start at 0!\n\n";
            }

            // Try to build and convert a the values to a TimeSpan and export it as a string.
            try
            {
                Numerics.ConvertTimeSpanToString(new TimeSpan(Convert.ToInt32(hoursNumericUpDown.Value), Convert.ToInt32(minutesNumericUpDown.Value), Convert.ToInt32(secondsNumericUpDown.Value)));
            }
            catch
            {
                errTracker = false;
                errMessage += "TimeSpan conversion failed! Please check if your time values are within a reasonable range.\n\n";
            }

            // Sanity check
            try
            {
                TimeSpan ts = new TimeSpan(Convert.ToInt32(hoursNumericUpDown.Value), Convert.ToInt32(minutesNumericUpDown.Value), Convert.ToInt32(secondsNumericUpDown.Value));
                if (ts.TotalDays > 100)
                {
                    MessageBox.Show($"Your chosen time equates to {Math.Round(ts.TotalDays)} days ({Math.Round(ts.TotalDays / 365, 2)} years)!\n" +
                        "It is highly discouraged to choose such an insane amount of time as either your hardware, operating system, or this is app will fail *way* before you even come close to reaching the target!" +
                        "\n\nBut if you are actually going to do this, please tell me how long this app survived.",
                        "This isn't Stargate and your PC won't stand the test of time!", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
            }
            catch { }

            checkResult = errMessage;
            return errTracker;
        }

        /// <summary>
        /// Read application's startup arguments and process events
        /// </summary>
        private void ProcessArgs()
        {
            ExceptionHandler.LogEvent("[Menu] Process args");

            string timeArg = null;
            string controlMode = "Prefill"; // Use 'Prefill' control mode by default

            //Control Modes:
            //Prefill:      Prefills settings but let user manually change them too. Timer won't start automatically.
            //Lock:         Overrides settings so the user can not change them. Timer won't start automatically.
            //Launch:       Overrides settings and starts the timer.
            //ForcedLaunch: Overrides settings and starts the timer. Disables all UI controls and exit dialogs.

            // Read args and do some processing
            for (var i = 0; i < startupArgs.Length; i++)
            {
                switch (startupArgs[i])
                {
                    case "/SetTime":
                        timeArg = startupArgs[i + 1];
                        break;

                    case "/SetAction":
                        actionComboBox.Text = startupArgs[i + 1];
                        break;

                    case "/SetMode":
                        controlMode = startupArgs[i + 1];
                        break;

                    case "/Graceful":
                        gracefulCheckBox.Checked = true;
                        break;

                    case "/AllowSleep":
                        preventSleepCheckBox.Checked = false;
                        break;

                    case "/Background":
                        backgroundCheckBox.Checked = true;
                        break;
                }
            }

            // Process time arg
            if (!string.IsNullOrWhiteSpace(timeArg))
            {
                if (!timeArg.Contains(":"))
                {
                    // time in seconds
                    secondsNumericUpDown.Value = Convert.ToDecimal(timeArg);
                }
                else
                {
                    string[] splittedTimeArg = timeArg.Split(':');
                    int count = splittedTimeArg.Length - 1; // Count number of colons
                    switch (count)
                    {
                        case 0:
                            ExceptionHandler.LogEvent("[Menu] Invalid time args");
                            MessageBox.Show("StartupArgs Error: Please provide a valid argument after /SetTime", "Invalid argument", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            break;

                        case 1:
                            // Assuming HH:mm
                            hoursNumericUpDown.Value = Convert.ToDecimal(splittedTimeArg[0]);
                            minutesNumericUpDown.Value = Convert.ToDecimal(splittedTimeArg[1]);
                            break;

                        case 2:
                            // Assuming HH:mm:ss
                            hoursNumericUpDown.Value = Convert.ToDecimal(splittedTimeArg[0]);
                            minutesNumericUpDown.Value = Convert.ToDecimal(splittedTimeArg[1]);
                            secondsNumericUpDown.Value = Convert.ToDecimal(splittedTimeArg[2]);
                            break;
                    }
                }
            }

            // Process control mode
            switch (controlMode)
            {
                case "Prefill":
                    ExceptionHandler.LogEvent("[Menu] Setting 'Prefill' mode");
                    settingsButton.Enabled = false;
                    actionGroupBox.Enabled = true;
                    timeGroupBox.Enabled = true;
                    break;

                case "Lock":
                    ExceptionHandler.LogEvent("[Menu] Setting 'Lock' mode");
                    SettingsProvider.TemporaryMode = true;
                    startButton.Text = "Start (with recommended settings)";
                    settingsButton.Enabled = false;
                    actionGroupBox.Enabled = false;
                    timeGroupBox.Enabled = false;
                    break;

                case "Launch":
                    ExceptionHandler.LogEvent("[Menu] Settings 'Launch' mode");
                    SettingsProvider.TemporaryMode = true;
                    this.Hide();
                    StartCountdown();
                    break;

                case "ForcedLaunch":
                    ExceptionHandler.LogEvent("[Menu] Settings 'Launch' mode");
                    SettingsProvider.TemporaryMode = true;
                    this.Hide();
                    StartCountdown(true);
                    break;
            }
        }

        /// <summary>
        /// Load UI element data from settings
        /// </summary>
        private void LoadSettings()
        {
            ExceptionHandler.LogEvent("[Menu] Load settings");

            SettingsProvider.Load();

            actionComboBox.Text = SettingsProvider.Settings.DefaultTimer.Action;
            gracefulCheckBox.Checked = SettingsProvider.Settings.DefaultTimer.Graceful;
            preventSleepCheckBox.Checked = SettingsProvider.Settings.DefaultTimer.PreventSleep;
            backgroundCheckBox.Checked = SettingsProvider.Settings.DefaultTimer.Background;
            hoursNumericUpDown.Value = SettingsProvider.Settings.DefaultTimer.Hours;
            minutesNumericUpDown.Value = SettingsProvider.Settings.DefaultTimer.Minutes;
            secondsNumericUpDown.Value = SettingsProvider.Settings.DefaultTimer.Seconds;
        }

        /// <summary>
        /// Saves current timer settings as default settings if activated in settings
        /// </summary>
        private void SaveSettings()
        {
            ExceptionHandler.LogEvent("[Menu] Save settings");

            if (SettingsProvider.SettingsLoaded)
            {
                if (SettingsProvider.Settings.RememberLastState)
                {
                    SettingsProvider.Settings.DefaultTimer.Action = actionComboBox.Text;
                    SettingsProvider.Settings.DefaultTimer.Graceful = gracefulCheckBox.Checked;
                    SettingsProvider.Settings.DefaultTimer.PreventSleep = preventSleepCheckBox.Checked;
                    SettingsProvider.Settings.DefaultTimer.Background = backgroundCheckBox.Checked;
                    SettingsProvider.Settings.DefaultTimer.Hours = Convert.ToInt32(hoursNumericUpDown.Value);
                    SettingsProvider.Settings.DefaultTimer.Minutes = Convert.ToInt32(minutesNumericUpDown.Value);
                    SettingsProvider.Settings.DefaultTimer.Seconds = Convert.ToInt32(secondsNumericUpDown.Value);
                }

                SettingsProvider.Save();
            }
        }

        /// <summary>
        /// Starts the countdown with values from UI
        /// </summary>
        private void StartCountdown(bool forced = false)
        {
            ExceptionHandler.LogEvent("[Menu] Start countdown");

            // Calculate TimeSpan
            TimeSpan timeSpan = new TimeSpan(Convert.ToInt32(hoursNumericUpDown.Value), Convert.ToInt32(minutesNumericUpDown.Value), Convert.ToInt32(secondsNumericUpDown.Value));

            // Show countdown window
            using (Countdown countdown = new Countdown
            {
                CountdownTimeSpan = timeSpan,
                Action = actionComboBox.Text,
                Graceful = gracefulCheckBox.Checked,
                PreventSystemSleep = preventSleepCheckBox.Checked,
                UI = !backgroundCheckBox.Checked,
                Forced = forced,
                Password = password
            })
            {
                countdown.Owner = this;
                countdown.ShowDialog();
                Application.Exit(); // Exit application after countdown is closed
            }
        }
    }
}
