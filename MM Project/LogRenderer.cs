namespace MudProxyViewer;

/// <summary>
/// Handles rendering of log messages with ANSI color support
/// Extracted from MainForm for better code organization
/// </summary>
public class LogRenderer
{
    private int _logMessageCount = 0;
    
    /// <summary>
    /// Log a message with ANSI color code interpretation
    /// </summary>
    public void LogMessageWithAnsi(string message, MessageType type, 
        RichTextBox targetTextBox, CheckBox autoScrollCheckBox, bool showTimestamp)
    {
        // Trim log if needed
        _logMessageCount++;
        if (_logMessageCount % 100 == 0)
        {
            TrimLogIfNeeded(targetTextBox);
        }

        // Only add timestamps to system log, not MUD output
        if (type == MessageType.System && showTimestamp)
        {
            targetTextBox.SelectionStart = targetTextBox.TextLength;
            targetTextBox.SelectionColor = Color.Gray;
            targetTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
        }

        // Parse and render ANSI codes
        int pos = 0;
        Color currentColor = Color.FromArgb(192, 192, 192);  // Default gray
        bool isBold = false;

        while (pos < message.Length)
        {
            // Look for ANSI escape sequence
            int escPos = message.IndexOf('\x1B', pos);
            
            if (escPos == -1)
            {
                // No more escape sequences, output rest of string
                string remaining = message.Substring(pos);
                if (!string.IsNullOrEmpty(remaining))
                {
                    targetTextBox.SelectionStart = targetTextBox.TextLength;
                    targetTextBox.SelectionColor = isBold ? BrightenColor(currentColor) : currentColor;
                    targetTextBox.AppendText(remaining);
                }
                break;
            }

            // Output text before escape sequence
            if (escPos > pos)
            {
                string textBefore = message.Substring(pos, escPos - pos);
                targetTextBox.SelectionStart = targetTextBox.TextLength;
                targetTextBox.SelectionColor = isBold ? BrightenColor(currentColor) : currentColor;
                targetTextBox.AppendText(textBefore);
            }

            // Parse escape sequence - skip ESC character
            int seqEnd = escPos + 1;
            
            if (seqEnd < message.Length)
            {
                char nextChar = message[seqEnd];
                
                if (nextChar == '[')
                {
                    // CSI (Control Sequence Introducer) - ESC[
                    seqEnd++;
                    int codeStart = seqEnd;
                    
                    // Read parameters (digits and semicolons)
                    while (seqEnd < message.Length && (char.IsDigit(message[seqEnd]) || message[seqEnd] == ';' || message[seqEnd] == '?'))
                        seqEnd++;
                    
                    // Read the final character that determines the command
                    if (seqEnd < message.Length)
                    {
                        char cmdChar = message[seqEnd];
                        seqEnd++; // Skip command character
                        
                        if (cmdChar == 'm')
                        {
                            // SGR (Select Graphic Rendition) - colors
                            string codes = message.Substring(codeStart, seqEnd - codeStart - 1);
                            foreach (var code in codes.Split(';'))
                            {
                                if (int.TryParse(code, out int codeNum))
                                {
                                    switch (codeNum)
                                    {
                                        case 0: currentColor = Color.FromArgb(192, 192, 192); isBold = false; break;
                                        case 1: isBold = true; break;
                                        case 30: currentColor = Color.FromArgb(0, 0, 0); break;
                                        case 31: currentColor = Color.FromArgb(170, 0, 0); break;
                                        case 32: currentColor = Color.FromArgb(0, 170, 0); break;
                                        case 33: currentColor = Color.FromArgb(170, 85, 0); break;
                                        case 34: currentColor = Color.FromArgb(0, 0, 170); break;
                                        case 35: currentColor = Color.FromArgb(170, 0, 170); break;
                                        case 36: currentColor = Color.FromArgb(0, 170, 170); break;
                                        case 37: currentColor = Color.FromArgb(192, 192, 192); break;
                                    }
                                }
                            }
                        }
                        // All other CSI sequences (K, J, H, A, B, C, D, etc.) are silently ignored
                        // They're cursor/display control commands not applicable to RichTextBox
                    }
                }
                else if (nextChar == '(' || nextChar == ')')
                {
                    // Character set selection - ESC( or ESC) followed by one character
                    seqEnd++;
                    if (seqEnd < message.Length) seqEnd++; // Skip the charset identifier
                }
                else if (nextChar >= '0' && nextChar <= '9')
                {
                    // Some other escape with digit
                    seqEnd++;
                }
                else if (nextChar == '=' || nextChar == '>' || nextChar == '<')
                {
                    // Keypad mode
                    seqEnd++;
                }
                else if (nextChar == 'M' || nextChar == 'D' || nextChar == 'E' || nextChar == '7' || nextChar == '8')
                {
                    // Single character escapes (reverse index, index, next line, save/restore cursor)
                    seqEnd++;
                }
                else
                {
                    // Unknown escape - skip just the ESC
                    // seqEnd is already at escPos + 1
                }
            }
            
            pos = seqEnd;
        }

        // Add newline
        targetTextBox.AppendText(Environment.NewLine);

        // Auto-scroll
        if (autoScrollCheckBox.Checked)
        {
            targetTextBox.SelectionStart = targetTextBox.TextLength;
            targetTextBox.ScrollToCaret();
        }
    }

    /// <summary>
    /// Log a simple message with color coding by type
    /// </summary>
    public void LogMessage(string message, MessageType type, 
        RichTextBox targetTextBox, CheckBox autoScrollCheckBox, bool showTimestamp)
    {
        Color color = type switch
        {
            MessageType.Server => Color.FromArgb(0, 255, 0),
            MessageType.Client => Color.FromArgb(0, 191, 255),
            MessageType.System => Color.FromArgb(255, 204, 0),
            _ => Color.White
        };

        string prefix = type == MessageType.Client ? ">>> " : "";
        string timestamp = showTimestamp ? $"[{DateTime.Now:HH:mm:ss}] " : "";

        // Trim log if it's getting too long (check every 100 messages for performance)
        _logMessageCount++;
        if (_logMessageCount % 100 == 0)
        {
            TrimLogIfNeeded(targetTextBox);
        }

        targetTextBox.SelectionStart = targetTextBox.TextLength;
        targetTextBox.SelectionLength = 0;

        if (!string.IsNullOrEmpty(timestamp))
        {
            targetTextBox.SelectionColor = Color.Gray;
            targetTextBox.AppendText(timestamp);
        }

        targetTextBox.SelectionColor = color;
        targetTextBox.AppendText(prefix + message + Environment.NewLine);

        // Auto-scroll: if checkbox is checked, scroll to bottom
        if (autoScrollCheckBox.Checked)
        {
            targetTextBox.SelectionStart = targetTextBox.TextLength;
            targetTextBox.ScrollToCaret();
        }
    }

    /// <summary>
    /// Brighten a color for bold/bright ANSI codes
    /// </summary>
    private static Color BrightenColor(Color color)
    {
        // Standard bright versions
        if (color == Color.FromArgb(0, 0, 0)) return Color.FromArgb(85, 85, 85);           // Bright black (gray)
        if (color == Color.FromArgb(170, 0, 0)) return Color.FromArgb(255, 85, 85);        // Bright red
        if (color == Color.FromArgb(0, 170, 0)) return Color.FromArgb(85, 255, 85);        // Bright green
        if (color == Color.FromArgb(170, 85, 0)) return Color.FromArgb(255, 255, 85);      // Bright yellow
        if (color == Color.FromArgb(0, 0, 170)) return Color.FromArgb(85, 85, 255);        // Bright blue
        if (color == Color.FromArgb(170, 0, 170)) return Color.FromArgb(255, 85, 255);     // Bright magenta
        if (color == Color.FromArgb(0, 170, 170)) return Color.FromArgb(85, 255, 255);     // Bright cyan
        if (color == Color.FromArgb(192, 192, 192)) return Color.FromArgb(255, 255, 255);  // Bright white
        return color;
    }

    /// <summary>
    /// Trim log if it exceeds maximum size
    /// </summary>
    private static void TrimLogIfNeeded(RichTextBox textBox)
    {
        // Use TextLength as a proxy for size - much faster than counting lines
        // Approximate: if text is over 500KB, trim it down
        const int MAX_TEXT_LENGTH = 500000;  // ~500KB
        const int TRIM_TO_LENGTH = 300000;   // ~300KB
        
        if (textBox.TextLength > MAX_TEXT_LENGTH)
        {
            try
            {
                textBox.SuspendLayout();
                
                // Calculate how much to remove
                int removeLength = textBox.TextLength - TRIM_TO_LENGTH;
                
                // Find a newline near the remove point to avoid cutting mid-line
                int actualRemovePoint = textBox.Text.IndexOf('\n', removeLength);
                if (actualRemovePoint == -1)
                    actualRemovePoint = removeLength;
                else
                    actualRemovePoint++;
                
                textBox.Select(0, actualRemovePoint);
                
                // Temporarily disable ReadOnly to prevent system beep
                textBox.ReadOnly = false;
                textBox.SelectedText = "";
                textBox.ReadOnly = true;
            }
            finally
            {
                textBox.ResumeLayout();
            }
        }
    }
}
