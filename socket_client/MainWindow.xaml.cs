using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace socket_client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Socket client;
        Task recvTask;
        CancellationTokenSource cts;

        public MainWindow()
        {
            InitializeComponent();
            Console.SetOut(new ControlWriter(logTextBox));
            sendButton.IsEnabled = false;
        }
        public static void LogWriteLine(string msg)
        {
            Console.WriteLine(string.Format("[{0}]{1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg));
        }

        public void NotifyClose()
        {
            JObject notify = new JObject();
            notify["type"] = "disconnect";
            client.Send(Encoding.UTF8.GetBytes(notify.ToString(Formatting.None)));
            //已经连接
            cts.Cancel();
            //recvTask.Wait();
            client.Shutdown(SocketShutdown.Both);
            connectButton.Content = "连接服务器";
            connectionText.Text = "未连接";
            connectionText.Foreground = Brushes.Red;
            sendButton.IsEnabled = false;
        }

        private async void connectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sendButton.IsEnabled == false)
            {
                //未连接
                LogWriteLine("开始连接");
                connectButton.IsEnabled = false;
                //test code
                int port = 9047;
                IPAddress addr;
                IPEndPoint ep;
                if (int.TryParse(serverPortTextBox.Text, out port) && IPAddress.TryParse(serverIPTextBox.Text, out addr))
                {
                    ep = new IPEndPoint(addr, port);
                    client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                }
                else
                {
                    string msg = "服务器IP或端口填写有误，请重新填写";
                    MessageBox.Show(msg);
                    LogWriteLine(msg);
                    return;
                }
                // 开始连接
                try
                {
                    await client.ConnectAsync(ep);
                }
                catch (Exception ex)
                {
                    LogWriteLine("连接失败，可能是网络不太行");
                    connectButton.IsEnabled = true;
                    return;
                }

                //连接成功
                cts = new CancellationTokenSource();
                recvTask = Task.Run(() => Receive(cts.Token));

                LogWriteLine("连接成功");
                connectButton.Content = "断开连接";
                connectButton.IsEnabled = true;
            }
            else
            {
                NotifyClose();
            }
        }

        private async void Receive(CancellationToken ct)
        {

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    byte[] buffer = new byte[1 << 20];  //1MB
                    int length = client.Receive(buffer);
                    if (length == 0) continue;
                    string message = Encoding.UTF8.GetString(buffer);


                    JObject res = JObject.Parse(message);
                    if (res["type"].ToString() == "ack")
                    {
                        //收到服务器ECHO应答，开始登录/注册
                        LogWriteLine("已连接到服务器，正在登录");
                        JObject o = new JObject();
                        o["type"] = "login";
                        o["username"] = userNameTextBox.Dispatcher.Invoke(() => userNameTextBox.Text);
                        o["password"] = tokenTextBox.Dispatcher.Invoke(() => tokenTextBox.Text);
                        byte[] login_buffer = Encoding.UTF8.GetBytes(o.ToString(Formatting.None));
                        client.Send(login_buffer);
                    }
                    else if (res["type"].ToString() == "login")
                    {
                        //登录信息的返回
                        if (res["status"].ToString() == "ok")
                        {
                            //登录成功
                            LogWriteLine("登录成功，欢迎加入群聊");
                            connectionText.Dispatcher.Invoke(() => connectionText.Text = "已连接");
                            connectionText.Dispatcher.Invoke(() => connectionText.Foreground = Brushes.Green);
                            sendButton.Dispatcher.Invoke(() => sendButton.IsEnabled = true);
                            //connectionText.Text = "已连接";
                            //connectionText.Foreground = System.Windows.Media.Brushes.Green;
                            //sendButton.IsEnabled = true;
                        }
                        else if (res["status"].ToString() == "registered")
                        {
                            //注册成功
                            LogWriteLine("注册成功，欢迎加入群聊");
                            connectionText.Dispatcher.Invoke(() => connectionText.Text = "已连接");
                            connectionText.Dispatcher.Invoke(() => connectionText.Foreground = Brushes.Green);
                            sendButton.Dispatcher.Invoke(() => sendButton.IsEnabled = true);
                            //connectionText.Text = "已连接";
                            //connectionText.Foreground = System.Windows.Media.Brushes.Green;
                            //sendButton.IsEnabled = true;
                        }
                        else
                        {
                            //登录注册失败
                            LogWriteLine("登录失败，请检查你的用户名和口令");
                            client.Close();
                            return;
                        }
                    }
                    else if (res["type"].ToString() == "text")
                    {
                        //发送消息的返回
                        if (res["status"].ToString() != "ok")
                        {
                            LogWriteLine("消息发送失败");
                        }
                    }
                    else if (res["type"].ToString() == "message")
                    {
                        //收到的其他消息
                        if (res["status"].ToString() == "ok")
                            LogWriteLine(res["sender"].ToString() + " : " + res["content"]);
                    }
                    else if (res["type"].ToString() == "shutdown")
                    {
                        //收到了服务器关闭的消息
                        cts.Cancel();
                        //recvTask.Wait();
                        client.Shutdown(SocketShutdown.Both);
                        connectionText.Dispatcher.Invoke(() => connectionText.Text = "未连接");
                        connectionText.Dispatcher.Invoke(() => connectionText.Foreground = Brushes.Red);
                        sendButton.Dispatcher.Invoke(() => sendButton.IsEnabled = false);
                        connectButton.Dispatcher.Invoke(() => connectButton.Content = "连接服务器");
                        LogWriteLine("服务器关闭了.");
                    }
                }
                catch (Exception ex)
                {
                    LogWriteLine("Error at " + ex.Source + ": " + ex.Message);
                    LogWriteLine(ex.StackTrace);
                }
            }
        }

        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            string message = inputTextBox.Text;
            JObject send = new JObject();
            send["type"] = "text";
            send["content"] = message;
            byte[] buffer = Encoding.UTF8.GetBytes(send.ToString(Formatting.None));
            if (client != null)
            {
                Task.Run(() => client.Send(buffer));
            }
        }

        private void _this_Closing(object sender, CancelEventArgs e)
        {
            NotifyClose();
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
    }
}
