namespace MudProxyViewer;

/// <summary>
/// Custom control to display a player's status with HP/Mana bars and buffs
/// </summary>
public class PlayerStatusPanel : Panel
{
    private readonly bool _isSelf;
    private string _playerName = "";
    private string _playerClass = "";
    private int _currentHp = 0;
    private int _maxHp = 0;
    private int _currentMana = 0;
    private int _maxMana = 0;
    private int _hpPercent = 100;
    private int _manaPercent = 100;
    private bool _isPoisoned = false;
    private bool _isResting = false;
    private string _resourceType = "Mana";  // "Mana" or "Kai"
    private readonly List<BuffDisplayInfo> _buffs = new();
    
    private const int PADDING = 4;
    private const int BAR_HEIGHT = 14;
    private const int BUFF_BAR_HEIGHT = 12;
    private const int NAME_HEIGHT = 16;
    
    // Dynamic buff name width - calculated based on panel width
    private int BuffNameWidth => Math.Max(50, Math.Min(120, (this.Width - PADDING * 2) / 3));
    
    public int CalculatedHeight { get; private set; }
    
    public PlayerStatusPanel(bool isSelf = false)
    {
        _isSelf = isSelf;
        this.DoubleBuffered = true;
        this.BackColor = isSelf ? Color.FromArgb(35, 40, 45) : Color.FromArgb(30, 30, 30);
        
        // Calculate initial height
        UpdateHeight();
    }
    
    public void UpdatePlayer(string name, string playerClass, int hpPercent, int manaPercent, 
        bool isPoisoned = false, bool isResting = false, string resourceType = "Mana")
    {
        _playerName = name;
        _playerClass = playerClass;
        _hpPercent = Math.Clamp(hpPercent, 0, 100);
        _manaPercent = Math.Clamp(manaPercent, 0, 100);
        _isPoisoned = isPoisoned;
        _isResting = isResting;
        _resourceType = resourceType;
        // Clear actual values since we're using percentages
        _currentHp = 0;
        _maxHp = 0;
        _currentMana = 0;
        _maxMana = 0;
        UpdateHeight();
        Invalidate();
    }
    
    public void UpdatePlayerExact(string name, string playerClass, int currentHp, int maxHp, 
        int currentMana, int maxMana, bool isPoisoned = false, bool isResting = false, string resourceType = "Mana")
    {
        _playerName = name;
        _playerClass = playerClass;
        _currentHp = currentHp;
        _maxHp = maxHp;
        _currentMana = currentMana;
        _maxMana = maxMana;
        _hpPercent = maxHp > 0 ? (currentHp * 100 / maxHp) : 100;
        _manaPercent = maxMana > 0 ? (currentMana * 100 / maxMana) : 100;
        _isPoisoned = isPoisoned;
        _isResting = isResting;
        _resourceType = resourceType;
        UpdateHeight();
        Invalidate();
    }
    
    public void UpdateBuffs(IEnumerable<BuffDisplayInfo> buffs)
    {
        _buffs.Clear();
        _buffs.AddRange(buffs);
        UpdateHeight();
        Invalidate();
    }
    
    public void ClearBuffs()
    {
        _buffs.Clear();
        UpdateHeight();
        Invalidate();
    }
    
    private bool ShouldShowManaBar => _maxMana > 0 || (_isSelf && _manaPercent > 0);
    
    private void UpdateHeight()
    {
        int height = PADDING + NAME_HEIGHT + PADDING + BAR_HEIGHT;  // Name + HP bar
        
        // Add mana bar height if applicable
        if (ShouldShowManaBar)
        {
            height += 2 + BAR_HEIGHT;
        }
        
        height += PADDING;
        
        // Add height for buffs
        if (_buffs.Count > 0)
        {
            height += 2 + (_buffs.Count * (BUFF_BAR_HEIGHT + 2));
        }
        
        height += PADDING;
        CalculatedHeight = height;
        
        if (this.Height != height)
        {
            this.Height = height;
        }
    }
    
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        
        int y = PADDING;
        int barWidth = this.Width - (PADDING * 2);
        
        // Draw border for self
        if (_isSelf)
        {
            using var borderPen = new Pen(Color.FromArgb(80, 120, 180), 1);
            g.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
        }
        
        // Player name and class
        var nameText = string.IsNullOrEmpty(_playerName) ? "(Unknown)" : _playerName;
        if (!string.IsNullOrEmpty(_playerClass))
        {
            nameText += $" ({_playerClass})";
        }
        
        // Add status indicators
        if (_isResting) nameText += " [R]";
        if (_isPoisoned) nameText += " [P]";
        
        using (var nameFont = new Font("Segoe UI", _isSelf ? 9f : 8f, _isSelf ? FontStyle.Bold : FontStyle.Regular))
        using (var nameBrush = new SolidBrush(_isSelf ? Color.White : Color.FromArgb(200, 200, 200)))
        {
            g.DrawString(nameText, nameFont, nameBrush, PADDING, y);
        }
        y += NAME_HEIGHT + PADDING;
        
        // HP Bar - Always RED tinted
        DrawResourceBar(g, PADDING, y, barWidth, BAR_HEIGHT, 
            _hpPercent, GetHpColor(_hpPercent), 
            _maxHp > 0 ? $"HP {_currentHp}/{_maxHp}" : $"HP {_hpPercent}%");
        y += BAR_HEIGHT + 2;
        
        // Mana/Kai Bar - Only show if player has mana/kai
        if (ShouldShowManaBar)
        {
            var resourceLabel = _resourceType == "Kai" ? "Kai" : "Mana";
            DrawResourceBar(g, PADDING, y, barWidth, BAR_HEIGHT, 
                _manaPercent, Color.FromArgb(70, 130, 220), 
                _maxMana > 0 ? $"{resourceLabel} {_currentMana}/{_maxMana}" : $"{resourceLabel} {_manaPercent}%");
            y += BAR_HEIGHT;
        }
        y += PADDING;
        
        // Buffs - name on left, bar with timer on right
        foreach (var buff in _buffs)
        {
            DrawBuffBar(g, PADDING, y, barWidth, BUFF_BAR_HEIGHT, buff);
            y += BUFF_BAR_HEIGHT + 2;
        }
    }
    
    private void DrawResourceBar(Graphics g, int x, int y, int width, int height, 
        int percent, Color fillColor, string text)
    {
        // Background
        using (var bgBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
        {
            g.FillRectangle(bgBrush, x, y, width, height);
        }
        
        // Fill
        int fillWidth = (int)(width * percent / 100.0);
        if (fillWidth > 0)
        {
            using var fillBrush = new SolidBrush(fillColor);
            g.FillRectangle(fillBrush, x, y, fillWidth, height);
        }
        
        // Border
        using (var borderPen = new Pen(Color.FromArgb(60, 60, 60)))
        {
            g.DrawRectangle(borderPen, x, y, width - 1, height - 1);
        }
        
        // Text (centered)
        using var textFont = new Font("Consolas", 8f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var textSize = g.MeasureString(text, textFont);
        float textX = x + (width - textSize.Width) / 2;
        float textY = y + (height - textSize.Height) / 2;
        
        // Draw text shadow for readability
        using (var shadowBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            g.DrawString(text, textFont, shadowBrush, textX + 1, textY + 1);
        }
        g.DrawString(text, textFont, textBrush, textX, textY);
    }
    
    private void DrawBuffBar(Graphics g, int x, int y, int width, int height, BuffDisplayInfo buff)
    {
        int buffNameWidth = BuffNameWidth;
        
        // Draw buff name on the left (outside the bar)
        using var nameFont = new Font("Segoe UI", 7f);
        using var nameBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
        
        var nameText = buff.Name;
        var nameSize = g.MeasureString(nameText, nameFont);
        
        // Truncate name if too long
        while (nameSize.Width > buffNameWidth - 4 && nameText.Length > 3)
        {
            nameText = nameText.Substring(0, nameText.Length - 1);
            nameSize = g.MeasureString(nameText + "..", nameFont);
        }
        if (nameText != buff.Name)
        {
            nameText += "..";
        }
        
        float nameY = y + (height - nameSize.Height) / 2;
        g.DrawString(nameText, nameFont, nameBrush, x, nameY);
        
        // Bar starts after the name area
        int barX = x + buffNameWidth;
        int barWidth = width - buffNameWidth;
        
        // Background
        using (var bgBrush = new SolidBrush(Color.FromArgb(15, 15, 15)))
        {
            g.FillRectangle(bgBrush, barX, y, barWidth, height);
        }
        
        // Fill based on time remaining
        int fillWidth = (int)(barWidth * buff.PercentRemaining / 100.0);
        if (fillWidth > 0)
        {
            var fillColor = GetBuffColor(buff.SecondsRemaining);
            using var fillBrush = new SolidBrush(fillColor);
            g.FillRectangle(fillBrush, barX, y, fillWidth, height);
        }
        
        // Border
        using (var borderPen = new Pen(Color.FromArgb(50, 50, 50)))
        {
            g.DrawRectangle(borderPen, barX, y, barWidth - 1, height - 1);
        }
        
        // Timer text inside the bar (centered)
        var timeText = FormatTime(buff.SecondsRemaining);
        using var textFont = new Font("Consolas", 7f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var textSize = g.MeasureString(timeText, textFont);
        float textX = barX + (barWidth - textSize.Width) / 2;
        float textY = y + (height - textSize.Height) / 2;
        
        // Draw text shadow for readability
        using (var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
        {
            g.DrawString(timeText, textFont, shadowBrush, textX + 1, textY + 1);
        }
        g.DrawString(timeText, textFont, textBrush, textX, textY);
    }
    
    private Color GetHpColor(int percent)
    {
        // Always red-tinted to distinguish from buff bars
        if (percent > 75) return Color.FromArgb(180, 60, 60);      // Dark red (healthy)
        if (percent > 50) return Color.FromArgb(200, 80, 50);      // Red-orange
        if (percent > 25) return Color.FromArgb(220, 100, 40);     // Orange-red
        return Color.FromArgb(220, 40, 40);                         // Bright red (danger)
    }
    
    private Color GetBuffColor(double secondsRemaining)
    {
        if (secondsRemaining > 60) return Color.FromArgb(50, 150, 50);
        if (secondsRemaining > 30) return Color.FromArgb(180, 180, 50);
        if (secondsRemaining > 10) return Color.FromArgb(220, 140, 40);
        return Color.FromArgb(200, 50, 50);
    }
    
    private string FormatTime(double seconds)
    {
        if (seconds <= 0) return "0:00";
        if (seconds >= 60)
        {
            int mins = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            return $"{mins}:{secs:D2}";
        }
        return $"0:{(int)seconds:D2}";
    }
}

public class BuffDisplayInfo
{
    public string Name { get; set; } = "";
    public double SecondsRemaining { get; set; }
    public double PercentRemaining { get; set; }
    
    public BuffDisplayInfo() { }
    
    public BuffDisplayInfo(ActiveBuff buff)
    {
        Name = buff.Configuration.DisplayName;
        SecondsRemaining = buff.TimeRemaining.TotalSeconds;
        PercentRemaining = buff.PercentRemaining;
    }
}
