using System.Windows;
using System.Windows.Controls;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.Collections.Generic;
using System;
using System.Text;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace socket_server
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ServerSocket instance;
        bool isRunning = false;
        public MainWindow()
        {
            InitializeComponent();
            Console.SetOut(new ControlWriter(logTextBox));
        }

        private void portTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            int i;
            TextBox box = (TextBox)sender;
            bool is_valid = int.TryParse(box.Text, out i) && i >= 1024 && i <= 65535;
            if (!is_valid)
            {
                MessageBox.Show("端口应该在1024~65535之间");
                box.Text = "9047";
                box.Focus();
            }
        }

        private async void controlButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isRunning)
            {
                int port;
                if (int.TryParse(portTextBox.Text, out port))
                {
                    instance = new ServerSocket();
                    instance.StartServer(port);
                    statusLabel.Content = "正在运行";
                    statusLabel.Foreground = System.Windows.Media.Brushes.Green;
                    controlButton.Content = "关闭服务端";
                    isRunning = true;
                    ControlWriter.LogWriteLine("服务器开始运行");
                }
            }
            else
            {
                //关闭服务端
                try
                {
                    JObject notify = new JObject();
                    notify["type"] = "shutdown";
                    instance.Broadcast(notify.ToString());
                    await Task.Delay(1000);
                    instance.StopServer();
                    statusLabel.Content = "未运行";
                    statusLabel.Foreground = System.Windows.Media.Brushes.Red;
                    controlButton.Content = "打开服务端";
                    isRunning = false;
                    ControlWriter.LogWriteLine("服务器结束运行");
                }
                catch (Exception ex)
                {
                    ControlWriter.LogWriteLine(ex.Message);
                    ControlWriter.LogWriteLine(ex.StackTrace);
                }
            }
        }
    }

    public class ServerSocket
    {
        private Socket serverSocket;
        private bool isListening;
        private Thread threadWatch;
        private IPEndPoint endPoint;
        private Dictionary<string, Socket> clientSockets;
        private Task watchTask;
        private CancellationTokenSource cts;

        public ServerSocket() { }

        public void StartServer(int port)
        {
            if (serverSocket == null)
            {
                try
                {
                    isListening = true;
                    clientSockets = new Dictionary<string, Socket>();
                    serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    endPoint = new IPEndPoint(IPAddress.Any, port);     //0.0.0.0:port

                    serverSocket.Bind(endPoint);
                    serverSocket.Listen(100);   //最大连接数

                    //threadWatch = new Thread(StartListen);
                    //threadWatch.IsBackground = true;    //后台进程
                    //threadWatch.Start();
                    cts = new CancellationTokenSource();
                    watchTask = Task.Run(() => StartListen(cts.Token));
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message);
                }
            }
        }

        private async void StartListen(CancellationToken ct)
        {
            Socket? client = null;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (serverSocket == null) return;
                    Task t = Task.Run(() =>
                    {
                        try { client = serverSocket.Accept(); }     //Accept()是blocking的函数
                        catch (SocketException) { return; }
                    });
                    while (await Task.WhenAny(t, Task.Delay(500)) != t)  //所以为了随时取消（
                    {
                        if (ct.IsCancellationRequested) return;
                    }
                    //client = serverSocket.Accept();
                }
                catch (Exception e)
                {
                    //TODO: Error Handling
                }

                try
                {
                    try { if (client == null) return; var _ = client.RemoteEndPoint; }
                    catch (Exception e) { return; }
                    string remoteAddr = client.RemoteEndPoint.ToString();
                    if (!string.IsNullOrEmpty(remoteAddr))
                    {
                        clientSockets.Add(remoteAddr, client);  //连接成功，添加到客户端列表中
                        ControlWriter.LogWriteLine(remoteAddr + "客户端连接成功");
                    }

                    IPAddress clientIP = (client.RemoteEndPoint as IPEndPoint).Address;
                    int clientPort = (client.RemoteEndPoint as IPEndPoint).Port;
                    JObject ack = new JObject();
                    ack["type"] = "ack";
                    ack["port"] = clientPort.ToString();
                    byte[] sbuffer = Encoding.UTF8.GetBytes(ack.ToString());
                    client.Send(sbuffer);

                    //创建通信的线程
                    //Thread threadRecv = new Thread(Receive)
                    //{
                    //    IsBackground = true
                    //};
                    //threadRecv.Start(client);

                    Task recvTask = Task.Run(() => Receive(client));
                }
                catch (Exception ex)
                {
                    ControlWriter.LogWriteLine(ex.Message);
                    ControlWriter.LogWriteLine(ex.StackTrace);
                }
            }
        }

        private void Receive(Socket client)
        {
            if (client == null) return;
            while (client != null)
            {
                try
                {
                    byte[] buffer = new byte[1024 * 1024]; //1MB
                    int len = client.Receive(buffer);
                    if (len == 0) continue;
                    string message = Encoding.UTF8.GetString(buffer, 0, len);
                    ControlWriter.LogWriteLine(string.Format("客户端{0}: {1}", client.RemoteEndPoint.ToString(), message));


                    JObject request = JObject.Parse(message);
                    JObject response;
                    if (request["type"].ToString() == "login")
                    {
                        //登录/注册请求
                        response = new JObject();
                        response["type"] = "login";
                        response["status"] = new Random().Next(0, 2) == 0 ? "ok" : "registered";
                        client.Send(Encoding.UTF8.GetBytes(response.ToString(Formatting.None)));
                    }
                    else if (request["type"].ToString() == "disconnect")
                    {
                        ControlWriter.LogWriteLine(client.RemoteEndPoint.ToString() + "断开了服务器");
                        clientSockets.Remove(client.RemoteEndPoint.ToString());
                        client.Shutdown(SocketShutdown.Both);
                        client.Dispose();
                    } else if(request["type"].ToString() == "text")
                    {
                        //客户端发送了一条文本消息
                        string content = request["content"].ToString();
                        ControlWriter.LogWriteLine(client.RemoteEndPoint.ToString() + "发送了: " + content);
                        response = new JObject();
                        response["type"] = "text";
                        response["status"] = "ok";
                        client.Send(Encoding.UTF8.GetBytes(response.ToString(Formatting.None)));   //先返回确认收到了

                        response = new JObject();
                        response["type"] = "message";
                        response["status"] = "ok";
                        response["content"] = content;
                        response["sender"] = client.RemoteEndPoint.ToString();
                        Broadcast(response.ToString(Formatting.None)); //再广播消息
                    }

                    //if (clientSockets.Count > 0)
                    //{
                    //    foreach (var socket in clientSockets)
                    //    {
                    //        socket.Value.Send(Encoding.UTF8.GetBytes(string.Format("[{0}]{1}: {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), client.RemoteEndPoint.ToString(), received)));
                    //    }
                    //}

                }
                catch (Exception)
                {
                    //ControlWriter.LogWriteLine(client.RemoteEndPoint.ToString() + "断开了服务器");
                    //clientSockets.Remove(client.RemoteEndPoint.ToString());

                    //client.Close();
                    break;
                }
            }
        }

        public void Broadcast(string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            if (serverSocket != null && clientSockets.Count > 0)
                foreach (var socket in clientSockets)
                {
                    socket.Value.Send(buffer);
                }
        }

        public void StopServer()
        {
            try
            {
                cts.Cancel();
                watchTask.Wait();
                foreach (var socket in clientSockets)
                {
                    socket.Value.Shutdown(SocketShutdown.Both);
                }
                clientSockets.Clear();
                serverSocket.Close();
                serverSocket.Dispose();
            }
            catch (Exception ex)
            {
                //Todo
                ControlWriter.LogWriteLine(ex.Message);
                ControlWriter.LogWriteLine(ex.StackTrace);
            }
        }
    }
    public class ControlWriter : TextWriter
    {
        private TextBox textbox;
        public ControlWriter(TextBox textbox)
        {
            this.textbox = textbox;
        }

        public override void Write(char value)
        {
            textbox.Dispatcher.Invoke(new Action(() => textbox.Text += value));
        }

        public override void Write(string value)
        {
            textbox.Dispatcher.Invoke(new Action(() => textbox.Text += value));
        }

        public override Encoding Encoding
        {
            get { return Encoding.ASCII; }
        }

        public static void LogWriteLine(string msg)
        {
            Console.WriteLine(string.Format("[{0}]{1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg));
        }
    }
}

