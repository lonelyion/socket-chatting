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
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace socket_server
{
    public class User
    {
        public string UserName { get; set; }
        public string Token { get; set; }

        public User(string u, string t) => (UserName, Token) = (u, t);
    }
    /// <summary>
    /// Interaction logic for ServerWindow.xaml
    /// </summary>
    public partial class ServerWindow : Window
    {
        ServerSocket instance;
        bool isRunning = false;

        public ServerWindow()
        {
            InitializeComponent();
            Console.SetOut(new ControlWriter(logTextBox));

            Thread thread = new Thread(() =>
            {
                //Dictionary<string, string> test = new Dictionary<string, string>();
                //test.Add("1", "amiya");
                //test.Add("2", "doctor");
                while (true)
                {
                    if (instance != null && instance.clientNames != null)
                    {
                        var copy = new Dictionary<string, string>(instance.clientNames);
                        onlineUserListView.Dispatcher.Invoke(new Action(() =>
                        {
                            onlineUserListView.ItemsSource = copy;
                        }));
                    }
                    Thread.Sleep(2500);
                }
            })
            { IsBackground = true };
            thread.Start();
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
                    //onlineUserListView.ItemsSource = instance.users;
                    //Binding binding = new Binding()
                    //{
                    //    Source = instance.clientNames,
                    //    Mode = BindingMode.OneWay,
                    //    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    //};
                    //onlineUserListView.SetBinding(ListView.ItemsSourceProperty, binding);
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
                    logTextBox.Text += string.Format("[{0}]{1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "服务端结束运行\r\n");
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
        private IPEndPoint endPoint;
        private Dictionary<string, Socket> clientSockets;
        private Task watchTask;
        private CancellationTokenSource cts;
        private List<User> users;
        public Dictionary<string, string> clientNames;

        public ServerSocket() { LoadJson(); }
        public void LoadJson()
        {
            try
            {
                using (StreamReader reader = File.OpenText("users.json"))
                {
                    string json = reader.ReadToEnd();
                    users = JsonConvert.DeserializeObject<List<User>>(json);
                }
            }
            catch (Exception)
            {
                users = new List<User>();
            }
        }

        public void SaveJson()
        {
            using (StreamWriter writer = File.CreateText("users.json"))
            {
                string json = JsonConvert.SerializeObject(users);
                writer.WriteLine(json);
            }
        }
        public void StartServer(int port)
        {
            if (serverSocket == null)
            {
                try
                {
                    clientSockets = new Dictionary<string, Socket>();
                    clientNames = new Dictionary<string, string>();

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
            Socket client = null;

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
                catch (Exception)
                {
                    //TODO: Error Handling
                }

                try
                {
                    try { if (client == null || !client.Connected) return; var _ = client.RemoteEndPoint; }
                    catch (Exception) { return; }

                    string remoteAddr = client.RemoteEndPoint.ToString();
                    if (!string.IsNullOrEmpty(remoteAddr))
                    {
                        clientSockets.Add(remoteAddr, client);  //连接成功，
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
                    string remoteEndPoint = client.RemoteEndPoint.ToString();
                    ControlWriter.LogWriteLine(string.Format("<-收到{0}: {1}", remoteEndPoint, message));


                    JObject request = JObject.Parse(message);
                    JObject response = null;
                    if (request["type"].ToString() == "login")
                    {
                        //登录/注册请求
                        response = new JObject();
                        response["type"] = "login";
                        bool fail_flag = false;

                        User u = users.Find(x => x.UserName == request["username"].ToString());
                        if (u == null)
                        {
                            //用户名不存在
                            u = new User(request["username"].ToString(), request["token"].ToString());
                            users.Add(u);
                            clientNames.Add(remoteEndPoint, request["username"].ToString());
                            response["status"] = "registered";
                        }
                        else
                        {
                            //用户名存在
                            if (u.Token == request["token"].ToString())
                            {
                                response["status"] = "ok";
                                clientNames.Add(remoteEndPoint, request["username"].ToString());
                            }
                            else
                            {
                                response["status"] = "fail";
                                fail_flag = true;
                            }
                        }
                        
                        client.Send(Encoding.UTF8.GetBytes(response.ToString(Formatting.None)));

                        if(fail_flag)
                        {
                            clientSockets.Remove(remoteEndPoint);
                            clientNames.Remove(remoteEndPoint);
                            client.Shutdown(SocketShutdown.Both);
                            client.Dispose();
                        }

                    }
                    else if (request["type"].ToString() == "disconnect")
                    {
                        //断开连接请求
                        ControlWriter.LogWriteLine(clientNames[remoteEndPoint] + "断开了服务器");
                        clientSockets.Remove(remoteEndPoint);
                        clientNames.Remove(remoteEndPoint);
                        client.Shutdown(SocketShutdown.Both);
                        client.Dispose();
                    }
                    else if (request["type"].ToString() == "text")
                    {
                        //客户端发送了一条文本消息
                        string content = request["content"].ToString();
                        //ControlWriter.LogWriteLine(clientNames[remoteEndPoint] + "发送了: " + content);
                        response = new JObject();
                        response["type"] = "text";
                        response["status"] = "ok";
                        client.Send(Encoding.UTF8.GetBytes(response.ToString(Formatting.None)));   //先返回确认收到了

                        response = new JObject();
                        response["type"] = "message";
                        response["status"] = "ok";
                        response["content"] = content;
                        response["sender"] = clientNames[remoteEndPoint];
                        Broadcast(response.ToString(Formatting.None)); //再广播消息
                    }
                    else if (request["type"].ToString() == "query")
                    {
                        response = new JObject();
                        response["type"] = "query";
                        response["online_count"] = clientNames.Count;
                        response["status"] = "ok";
                        client.Send(Encoding.UTF8.GetBytes(response.ToString(Formatting.None)));
                    }
                    if(response != null)
                        ControlWriter.LogWriteLine(string.Format("->发送{0}: {1}", remoteEndPoint, response.ToString(Formatting.None)));
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
                clientNames.Clear();
                serverSocket.Close();
                serverSocket.Dispose();
            }
            catch (Exception)
            {
                //no op
            }
            finally
            {
                SaveJson();
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
            if (textbox.Dispatcher.Thread != Thread.CurrentThread)
            {
                textbox.Dispatcher.Invoke(new Action(() =>
                {
                    textbox.Text += value;
                    textbox.ScrollToEnd();
                }
            ));
            }
            else
            {
                textbox.Text += value;
                textbox.ScrollToEnd();
            }
        }

        public override void Write(string value)
        {
            if (textbox.Dispatcher.Thread != Thread.CurrentThread)
            {
                textbox.Dispatcher.Invoke(new Action(() =>
                {
                    textbox.Text += value;
                    textbox.ScrollToEnd();
                }
            ));
            }
            else
            {
                textbox.Text += value;
                textbox.ScrollToEnd();
            }
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

