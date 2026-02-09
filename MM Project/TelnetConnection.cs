using System.Net.Sockets;
using System.Text;

namespace MudProxyViewer;

/// <summary>
/// Handles telnet connection to MUD server
/// Manages TCP connection, IAC negotiation, and data transmission
/// Extracted from MainForm.cs to improve code organization
/// </summary>
public class TelnetConnection
{
    // Events for communication with MainForm
    public event Action<string>? OnDataReceived;           // Raw text data from server
    public event Action<bool, string>? OnStatusChanged;    // Connection state changes
    public event Action<string>? OnLogMessage;             // Log messages

    // Network components
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isConnected = false;
    
    // Reconnection state
    private int _connectionAttemptCount = 0;
    private bool _userRequestedDisconnect = false;
    
    /// <summary>
    /// Connect to the MUD server with automatic reconnection
    /// </summary>
    public async Task ConnectAsync(string address, int port, BbsSettings settings)
    {
        _userRequestedDisconnect = false;
        _connectionAttemptCount = 0;
        
        bool shouldRetry = settings.ReconnectOnConnectionFail;
        int maxAttempts = settings.MaxConnectionAttempts;
        int retryPauseSeconds = settings.ConnectionRetryPauseSeconds;
        
        while (!_userRequestedDisconnect)
        {
            try
            {
                _connectionAttemptCount++;
                
                // Check max attempts
                if (maxAttempts > 0 && _connectionAttemptCount > maxAttempts)
                {
                    OnLogMessage?.Invoke($"Maximum connection attempts ({maxAttempts}) reached.");
                    OnStatusChanged?.Invoke(false, "Max attempts reached");
                    break;
                }
                
                // Log attempt
                if (_connectionAttemptCount > 1)
                {
                    OnLogMessage?.Invoke($"Connection attempt {_connectionAttemptCount}" + 
                        (maxAttempts > 0 ? $" of {maxAttempts}" : "") + "...");
                }
                else
                {
                    OnLogMessage?.Invoke($"Connecting to {address}:{port}...");
                }
                
                OnStatusChanged?.Invoke(false, "Connecting...");
                
                // Create connection
                _cancellationTokenSource = new CancellationTokenSource();
                _tcpClient = new TcpClient();
                
                await _tcpClient.ConnectAsync(address, port);
                _stream = _tcpClient.GetStream();
                _isConnected = true;
                
                OnLogMessage?.Invoke($"✓ Connected to {address}:{port}");
                OnStatusChanged?.Invoke(true, "Connected");
                
                // Start reading from server - this will return when connection is lost
                await ReadServerDataAsync(_cancellationTokenSource.Token);
                
                // If we get here, connection was lost (not user-initiated disconnect)
                if (!_userRequestedDisconnect && settings.ReconnectOnConnectionLost)
                {
                    OnLogMessage?.Invoke("Connection lost. Will attempt to reconnect...");
                    CleanupConnection();
                    _connectionAttemptCount = 0;  // Reset for reconnection attempts
                    shouldRetry = true;
                    // Continue the while loop to reconnect
                }
                else
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled - don't retry
                break;
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"Connection failed: {ex.Message}");
                CleanupConnection();
                
                if (!shouldRetry || _userRequestedDisconnect)
                {
                    OnStatusChanged?.Invoke(false, "Connection failed");
                    break;
                }
                
                // Wait before retry
                OnStatusChanged?.Invoke(false, $"Retrying in {retryPauseSeconds}s...");
                OnLogMessage?.Invoke($"Waiting {retryPauseSeconds} seconds before retry...");
                
                try
                {
                    await Task.Delay(retryPauseSeconds * 1000, _cancellationTokenSource?.Token ?? CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        
        // Final cleanup
        if (!_isConnected)
        {
            Disconnect();
        }
    }
    
    /// <summary>
    /// Disconnect from server
    /// </summary>
    public void Disconnect()
    {
        _userRequestedDisconnect = true;
        _isConnected = false;
        _cancellationTokenSource?.Cancel();
        
        CleanupConnection();
        
        OnLogMessage?.Invoke("Disconnected.");
        OnStatusChanged?.Invoke(false, "Disconnected");
    }
    
    /// <summary>
    /// Send raw byte data to server
    /// </summary>
    public async Task SendDataAsync(byte[] data)
    {
        if (_stream == null || !_isConnected) return;
        
        try
        {
            await _stream.WriteAsync(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Send error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Send a text command to the server (adds CR+LF)
    /// </summary>
    public async Task SendCommandAsync(string command)
    {
        if (_stream == null || !_isConnected)
        {
            OnLogMessage?.Invoke("Cannot send command - not connected");
            return;
        }
        
        try
        {
            var data = Encoding.GetEncoding(437).GetBytes(command + "\r\n");
            await _stream.WriteAsync(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Send error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Clean up connection resources
    /// </summary>
    private void CleanupConnection()
    {
        _isConnected = false;
        
        try { _stream?.Close(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        
        _stream = null;
        _tcpClient = null;
    }
    
    /// <summary>
    /// Read data from server and process telnet protocol
    /// </summary>
    private async Task ReadServerDataAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream != null)
            {
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0) break;
                
                // Process telnet IAC commands and get clean data
                var (cleanData, iacResponses) = ProcessTelnetData(buffer, bytesRead);
                
                // Send any IAC responses back to server
                foreach (var response in iacResponses)
                {
                    await SendDataAsync(response);
                }
                
                if (cleanData.Length > 0)
                {
                    string text = Encoding.GetEncoding(437).GetString(cleanData);
                    OnDataReceived?.Invoke(text);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                OnLogMessage?.Invoke($"Read error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Process telnet IAC (Interpret As Command) sequences
    /// Returns clean data with IAC commands stripped, plus any responses to send
    /// Properly negotiates terminal capabilities (ANSI, NAWS, etc.)
    /// </summary>
    private (byte[] cleanData, List<byte[]> responses) ProcessTelnetData(byte[] buffer, int length)
    {
        const byte IAC = 255;   // Interpret As Command
        const byte WILL = 251;
        const byte WONT = 252;
        const byte DO = 253;
        const byte DONT = 254;
        const byte SB = 250;    // Subnegotiation Begin
        const byte SE = 240;    // Subnegotiation End
        
        // Telnet options
        const byte ECHO = 1;    // Echo
        const byte SGA = 3;     // Suppress Go Ahead
        const byte TTYPE = 24;  // Terminal Type
        const byte NAWS = 31;   // Negotiate About Window Size
        
        const byte TELQUAL_IS = 0;
        const byte TELQUAL_SEND = 1;

        var cleanData = new List<byte>();
        var responses = new List<byte[]>();
        int i = 0;

        while (i < length)
        {
            if (buffer[i] == IAC && i + 1 < length)
            {
                byte command = buffer[i + 1];

                if (command == IAC)
                {
                    // Escaped IAC (255 255) = literal 255
                    cleanData.Add(IAC);
                    i += 2;
                }
                else if (command == SB)
                {
                    // Subnegotiation - find IAC SE
                    int j = i + 2;
                    while (j < length - 1)
                    {
                        if (buffer[j] == IAC && buffer[j + 1] == SE) break;
                        j++;
                    }
                    if (j >= length - 1)
                    {
                        i = length; // incomplete, wait for more data
                        break;
                    }
                    
                    if (i + 2 < length)
                    {
                        byte opt = buffer[i + 2];
                        
                        // Handle terminal type request
                        if (opt == TTYPE && j > i + 3 && buffer[i + 3] == TELQUAL_SEND)
                        {
                            // Server asks for terminal type - send "ANSI"
                            byte[] termType = Encoding.ASCII.GetBytes("ANSI");
                            byte[] response = new byte[6 + termType.Length];
                            response[0] = IAC;
                            response[1] = SB;
                            response[2] = TTYPE;
                            response[3] = TELQUAL_IS;
                            Buffer.BlockCopy(termType, 0, response, 4, termType.Length);
                            response[4 + termType.Length] = IAC;
                            response[5 + termType.Length] = SE;
                            responses.Add(response);
                            OnLogMessage?.Invoke("📡 Sent terminal type: ANSI");
                        }
                    }
                    
                    i = j + 2;
                }
                else if ((command == WILL || command == WONT || command == DO || command == DONT) && i + 2 < length)
                {
                    byte option = buffer[i + 2];
                    
                    if (command == DO)
                    {
                        // Server asks us to enable an option
                        if (option == NAWS || option == TTYPE || option == SGA)
                        {
                            // Accept these options
                            responses.Add(new byte[] { IAC, WILL, option });
                            
                            // If NAWS, send our window size
                            if (option == NAWS)
                            {
                                SendWindowSize(responses);
                            }
                        }
                        else
                        {
                            // Refuse unknown options
                            responses.Add(new byte[] { IAC, WONT, option });
                        }
                    }
                    else if (command == DONT)
                    {
                        // Server tells us to disable - acknowledge
                        responses.Add(new byte[] { IAC, WONT, option });
                    }
                    else if (command == WILL)
                    {
                        // Server offers to enable an option
                        if (option == ECHO || option == SGA)
                        {
                            // Accept echo and suppress-go-ahead
                            responses.Add(new byte[] { IAC, DO, option });
                        }
                        else
                        {
                            // Refuse unknown options
                            responses.Add(new byte[] { IAC, DONT, option });
                        }
                    }
                    else if (command == WONT)
                    {
                        // Server won't do something - acknowledge
                        responses.Add(new byte[] { IAC, DONT, option });
                    }
                    
                    i += 3;
                }
                else
                {
                    // Unknown command, skip IAC and command byte
                    i += 2;
                }
            }
            else
            {
                cleanData.Add(buffer[i]);
                i++;
            }
        }

        return (cleanData.ToArray(), responses);
    }
    
    /// <summary>
    /// Build NAWS (window size) subnegotiation and add to responses
    /// </summary>
    private void SendWindowSize(List<byte[]> responses)
    {
        const byte IAC = 255;
        const byte SB = 250;
        const byte SE = 240;
        const byte NAWS = 31;
        
        // Use a reasonable terminal size (80x24 is standard)
        int cols = 80;
        int rows = 24;
        
        byte[] naws = new byte[9];
        naws[0] = IAC;
        naws[1] = SB;
        naws[2] = NAWS;
        naws[3] = (byte)((cols >> 8) & 0xFF);
        naws[4] = (byte)(cols & 0xFF);
        naws[5] = (byte)((rows >> 8) & 0xFF);
        naws[6] = (byte)(rows & 0xFF);
        naws[7] = IAC;
        naws[8] = SE;
        
        responses.Add(naws);
        OnLogMessage?.Invoke($"📡 Sent window size: {cols}x{rows}");
    }
}
