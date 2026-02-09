using System;
using System.Drawing;
using System.Windows.Forms;

namespace MudProxyViewer.Controls
{
    /// <summary>
    /// Combat status panel showing tick timer, HP/Mana bars, and party status.
    /// Extracted from MainForm to reduce complexity.
    /// </summary>
    public class CombatStatusPanel : UserControl
    {
        // UI Controls
        private Label _lblCombatState = null!;
        private Label _lblNextTick = null!;
        private ProgressBar _progressTick = null!;
        private ProgressBar _progressHP = null!;
        private ProgressBar _progressMana = null!;
        private Label _lblHP = null!;
        private Label _lblMana = null!;
        private Panel _partyPanel = null!;
        private ListView _lvParty = null!;
        
        // State
        private bool _inCombat;
        private double _tickTimeRemaining;
        private int _currentHP;
        private int _maxHP;
        private int _currentMana;
        private int _maxMana;

        public CombatStatusPanel()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.BackColor = Color.FromArgb(45, 45, 45);
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(8);

            // Combat State Section
            var statePanel = CreateSection("Combat State", 0);
            _lblCombatState = new Label
            {
                Text = "Idle",
                ForeColor = Color.LimeGreen,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 5)
            };
            statePanel.Controls.Add(_lblCombatState);

            // Tick Timer Section
            var tickPanel = CreateSection("Next Tick", 80);
            _lblNextTick = new Label
            {
                Text = "--",
                ForeColor = Color.White,
                Font = new Font("Consolas", 9),
                AutoSize = true,
                Location = new Point(10, 5)
            };
            _progressTick = new ProgressBar
            {
                Location = new Point(10, 25),
                Size = new Size(statePanel.Width - 20, 20),
                Style = ProgressBarStyle.Continuous
            };
            tickPanel.Controls.AddRange(new Control[] { _lblNextTick, _progressTick });

            // Self Status Section
            var selfPanel = CreateSection("Self Status", 180);
            
            _lblHP = new Label
            {
                Text = "HP: --",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                Location = new Point(10, 5)
            };
            _progressHP = new ProgressBar
            {
                Location = new Point(10, 25),
                Size = new Size(selfPanel.Width - 20, 18),
                ForeColor = Color.FromArgb(0, 150, 0)
            };

            _lblMana = new Label
            {
                Text = "MA: --",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                Location = new Point(10, 50)
            };
            _progressMana = new ProgressBar
            {
                Location = new Point(10, 70),
                Size = new Size(selfPanel.Width - 20, 18),
                ForeColor = Color.FromArgb(0, 100, 200)
            };

            selfPanel.Controls.AddRange(new Control[] { _lblHP, _progressHP, _lblMana, _progressMana });

            // Party Section
            _partyPanel = CreateSection("Party", 320);
            _lvParty = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                Location = new Point(10, 5),
                Size = new Size(_partyPanel.Width - 20, _partyPanel.Height - 15),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            _lvParty.Columns.Add("Name", 120);
            _lvParty.Columns.Add("HP", 60);
            _lvParty.Columns.Add("MA", 60);
            _partyPanel.Controls.Add(_lvParty);

            this.Controls.AddRange(new Control[] { statePanel, tickPanel, selfPanel, _partyPanel });
        }

        private Panel CreateSection(string title, int yPos)
        {
            var panel = new Panel
            {
                BackColor = Color.FromArgb(35, 35, 35),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(8, yPos),
                Size = new Size(this.Width - 16, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 25,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            panel.Controls.Add(titleLabel);

            return panel;
        }

        #region Public Methods

        /// <summary>
        /// Update combat state (in combat vs idle)
        /// </summary>
        public void UpdateCombatState(bool inCombat)
        {
            _inCombat = inCombat;
            _lblCombatState.Text = inCombat ? "In Combat" : "Idle";
            _lblCombatState.ForeColor = inCombat ? Color.Red : Color.LimeGreen;
        }

        /// <summary>
        /// Update tick countdown timer
        /// </summary>
        public void UpdateTickTimer(double remainingSeconds, double totalSeconds)
        {
            _tickTimeRemaining = remainingSeconds;
            _lblNextTick.Text = $"{remainingSeconds:F1}s";
            
            // Update progress bar
            int percentage = totalSeconds > 0 
                ? (int)((remainingSeconds / totalSeconds) * 100) 
                : 0;
            _progressTick.Value = Math.Clamp(percentage, 0, 100);

            // Color coding
            if (remainingSeconds > 2.0)
                _lblNextTick.ForeColor = Color.LimeGreen;
            else if (remainingSeconds > 1.0)
                _lblNextTick.ForeColor = Color.Orange;
            else
                _lblNextTick.ForeColor = Color.Red;
        }

        /// <summary>
        /// Update HP and Mana bars
        /// </summary>
        public void UpdateSelfStatus(int currentHP, int maxHP, int currentMana, int maxMana)
        {
            _currentHP = currentHP;
            _maxHP = maxHP;
            _currentMana = currentMana;
            _maxMana = maxMana;

            // HP
            int hpPercent = maxHP > 0 ? (currentHP * 100) / maxHP : 0;
            _lblHP.Text = $"HP: {currentHP}/{maxHP} ({hpPercent}%)";
            _progressHP.Maximum = maxHP;
            _progressHP.Value = Math.Clamp(currentHP, 0, maxHP);

            // Mana
            int manaPercent = maxMana > 0 ? (currentMana * 100) / maxMana : 0;
            _lblMana.Text = $"MA: {currentMana}/{maxMana} ({manaPercent}%)";
            _progressMana.Maximum = maxMana;
            _progressMana.Value = Math.Clamp(currentMana, 0, maxMana);
        }

        /// <summary>
        /// Update party member list
        /// </summary>
        public void UpdateParty(List<PartyMember> members)
        {
            _lvParty.Items.Clear();
            
            foreach (var member in members)
            {
                var item = new ListViewItem(member.Name);
                item.SubItems.Add($"{member.HPPercent}%");
                item.SubItems.Add($"{member.ManaPercent}%");
                
                // Color code low HP
                if (member.HPPercent < 30)
                    item.ForeColor = Color.Red;
                else if (member.HPPercent < 60)
                    item.ForeColor = Color.Yellow;
                
                _lvParty.Items.Add(item);
            }
        }

        /// <summary>
        /// Reset all displays to default state
        /// </summary>
        public void Reset()
        {
            UpdateCombatState(false);
            _lblNextTick.Text = "--";
            _lblNextTick.ForeColor = Color.White;
            _progressTick.Value = 0;
            
            _lblHP.Text = "HP: --";
            _progressHP.Value = 0;
            _lblMana.Text = "MA: --";
            _progressMana.Value = 0;
            
            _lvParty.Items.Clear();
        }

        #endregion

        // Simple data structure for party members
        public class PartyMember
        {
            public string Name { get; set; } = "";
            public int HPPercent { get; set; }
            public int ManaPercent { get; set; }
        }
    }
}
