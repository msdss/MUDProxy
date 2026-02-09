namespace MudProxyViewer;

/// <summary>
/// Custom renderer for dark theme menus
/// </summary>
public class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }
    
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected)
        {
            using var brush = new SolidBrush(Color.FromArgb(70, 70, 70));
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(45, 45, 45));
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }
    }
    
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // Draw dark background for check area
        var rect = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, 
                                  e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
        using var bgBrush = new SolidBrush(Color.FromArgb(60, 60, 60));
        e.Graphics.FillRectangle(bgBrush, rect);
        
        // Draw border
        using var borderPen = new Pen(Color.FromArgb(100, 100, 100));
        e.Graphics.DrawRectangle(borderPen, rect);
        
        // Draw checkmark in white/light gray
        using var checkPen = new Pen(Color.FromArgb(200, 200, 200), 2);
        var checkRect = e.ImageRectangle;
        int x = checkRect.X + 3;
        int y = checkRect.Y + checkRect.Height / 2;
        e.Graphics.DrawLine(checkPen, x, y, x + 3, y + 3);
        e.Graphics.DrawLine(checkPen, x + 3, y + 3, x + 9, y - 3);
    }
    
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Color.FromArgb(45, 45, 45));
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
    
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(70, 70, 70));
        int y = e.Item.ContentRectangle.Height / 2;
        e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
    }
}
