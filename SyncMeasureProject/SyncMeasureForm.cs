using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using ZedGraph;



namespace SyncMeasureProject
{
    public partial class SyncMeasureForm : Form
    {
        List<PC> connection = new List<PC>();
        private int PORT = 8888; // TCP端口号默认为8888
        static Socket serverSocket;
        bool autorefresh = true;
        string inputPc1, inputPc2;
        long[] lastOffset30 = new long[30];
        long average = 0;
        int ret = 0;
        bool top = false;
        long preOffset = 0;
        PointPairList pointPairList = new PointPairList();
        LineItem myCurve;
        public SyncMeasureForm()
        {
            InitializeComponent();
            zedGraphControl1.IsEnableVZoom = true;    // 允许缩放
            zedGraphControl1.IsShowPointValues = true;  // 鼠标悬停显示数值
            GraphPane myPane = zedGraphControl1.GraphPane;//使用bin文件夹中的ZedGraph控件
            myPane.Title.Text = "OFFSET";          //标题
            myPane.XAxis.Title.Text = "Time";     //横坐标
            myPane.YAxis.Title.Text = "Offset ( us )";  //纵坐标  
            myPane.YAxis.Scale.MaxAuto = true;   //自动调整Y轴最大值
            myPane.YAxis.Scale.MinAuto = true;   //自动调整Y轴最小值
            myPane.YAxis.Scale.MinorStepAuto = true;   // 自动调整Y轴小步长
            myPane.YAxis.Scale.MajorStepAuto = true;   // 自动调整Y轴主步长
            myPane.XAxis.Scale.MinorStep = 5;
            myPane.XAxis.Scale.MajorStep = 5;
            myPane.Title.FontSpec.Size = 22;
            myPane.XAxis.Title.FontSpec.Size = 16;
            myPane.YAxis.Title.FontSpec.Size = 16;
            System.Timers.Timer timer = new System.Timers.Timer(1000);
            timer.Elapsed += new System.Timers.ElapsedEventHandler(refresh);
            timer.AutoReset = true;
            timer.Enabled = true;
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(SyncMeasureForm_FormClosed);
        }
        private void SyncMeasureForm_FormClosed(Object sender, EventArgs e)
        {
            System.Environment.Exit(0);
        }
        public void refresh(object source, System.Timers.ElapsedEventArgs e)
        {
            if ((comboBox1.SelectedIndex >= 0) && (comboBox2.SelectedIndex >= 0))
            {
                int timeNs1 = 0, timeNs2 = 0, timeStamp1 = 0, timeStamp2 = 0;
                long offset, timeS1 = 0, timeS2 = 0;
                long absOffset;
                string sign;
                foreach (PC pc in connection)
                {
                    if (string.Equals(pc.PcName, comboBox1.Text))
                    {
                        timeStamp1 = pc.TimeStamp;
                        timeS1 = pc.TimeS;
                        timeNs1 = pc.TimeNs;
                    }
                    if (string.Equals(pc.PcName, comboBox2.Text))
                    {
                        timeStamp2 = pc.TimeStamp;
                        timeS2 = pc.TimeS;
                        timeNs2 = pc.TimeNs;
                    }
                }
                if ((timeNs1 != 0 & timeNs2 != 0) && (timeStamp1 == timeStamp2))
                {
                    if (!string.Equals(inputPc1, comboBox1.Text) || !string.Equals(inputPc2, comboBox2.Text))
                    {
                        inputPc1 = comboBox1.Text;
                        inputPc2 = comboBox2.Text;
                        listBox1.Items.Clear();
                    }
                    richTextBox1.Text = "vm1: stamp:" + timeStamp1.ToString("D6") + " s:" + timeS1 + " ns:" + timeNs1 + "\r\n" + "vm2: stamp:" + timeStamp2.ToString("D6") + " s:" + timeS2 + " ns:" + timeNs2;
                    offset = (timeS1 * 1000000000 + timeNs1) - (timeS2 * 1000000000 + timeNs2);
                    if (offset >= 0)
                    {
                        sign = "";
                        absOffset = offset;
                    }
                    else
                    {
                        sign = "-";
                        absOffset = -offset;
                    }
                    zedGraphControl1.GraphPane.XAxis.Scale.MaxAuto = true;
                    pointPairList.Add((stamptosecond(timeStamp1)), offset / 1000);
                    this.zedGraphControl1.AxisChange();
                    this.zedGraphControl1.Invalidate();   
                    //this.zedGraphControl1.Refresh();
                    if (pointPairList.Count >= 50)
                    {
                        pointPairList.RemoveAt(0);
                    }
                    textBox1.Text = sign + (absOffset / 1000000000).ToString();
                    textBox2.Text = (absOffset % 1000000000).ToString("D9").Substring(0, 3);
                    textBox3.Text = (absOffset % 1000000000).ToString("D9").Substring(3, 3);
                    textBox4.Text = (absOffset % 1000000000).ToString("D9").Substring(6, 3);
                    if (offset != preOffset)
                    {
                        record(timeStamp1.ToString("D6") + ": " + "offset: " + sign + (absOffset / 1000000000).ToString() + "." + (absOffset % 1000000000).ToString("D9") + " s");
                        preOffset = offset;
                        if (ret == 30)
                        {
                            ret = 0;
                            top = true;
                        }
                        if (ret < 30)
                        {
                            lastOffset30[ret] = offset;
                        }
                        if (top)
                        {
                            long all = 0;
                            for (int i = 0; i < 30; i++)
                            {
                                all += lastOffset30[i];
                            }
                            average = (long)Math.Round((double)(all / 30), 0);
                            ret += 1;
                        }
                        if (!top)
                        {
                            long all = 0;
                            for (int i = 0; i <= ret; i++)
                            {
                                all += lastOffset30[i];
                            }
                            average = (long)Math.Round((double)(all / (ret + 1)), 0);
                            ret += 1;
                        }
                        if (average >= 0)
                        {
                            textBox5.Text = (average / 1000000000).ToString() + "." + (average % 1000000000).ToString("D9") + " s";
                        }
                        if (average < 0)
                        {
                            textBox5.Text = "-" + (-average / 1000000000).ToString() + "." + (-average % 1000000000).ToString("D9") + " s";
                        }
                    }
                }
            }
        }
        private void SyncMeasureForm_Load(object sender, EventArgs e)
        {
            portTextBox.Text = "8888";
            myCurve = zedGraphControl1.GraphPane.AddCurve("Offset between two PCs", pointPairList, Color.DarkGreen, SymbolType.Circle);
            myCurve.Line.Width = 2.0F;
        }
        private void server(Object sk)
        {

            while (true)
            {
                Socket s = (Socket)sk;
                Socket clientSocket = s.Accept();
                Thread reciveTh = new Thread(oneConnection);
                reciveTh.Start(clientSocket);
            }
        }
        private void oneConnection(Object clientSocket)
        {
            long timeS;
            int timeStamp, timeNs;
            byte[] result = new byte[1024];
            PC thispc = new PC();
            connection.Add(thispc);
            Socket myClientSocket = (Socket)clientSocket;
            thispc.PcName = myClientSocket.RemoteEndPoint.ToString();//将客户端ip地址和端口号作为名字
            addpcTocomboBox1(thispc.PcName);
            addpcTocomboBox2(thispc.PcName);
            while (true)
            {
                try
                {
                    int ret = myClientSocket.Receive(result);
                    if (ret == 0)
                    {
                        removepcIncomboBox1(thispc.PcName);
                        removepcIncomboBox2(thispc.PcName);
                        foreach (PC pc in connection)
                        {
                            if (string.Equals(pc.PcName, thispc.PcName))
                            {
                                connection.Remove(pc);
                            }
                        }
                        return;
                    }
                    timeStamp = BitConverter.ToInt32(result, 0) * 10000 + BitConverter.ToInt32(result, 4) * 100 + BitConverter.ToInt32(result, 8);
                    timeS = BitConverter.ToInt64(result, 12);
                    timeNs = BitConverter.ToInt32(result, 20);
                    Array.Clear(result, 0, result.Length);
                    foreach (PC pc in connection)
                    {
                        if (string.Equals(pc.PcName, thispc.PcName))
                        {
                            pc.TimeStamp = timeStamp;
                            pc.TimeS = timeS;
                            pc.TimeNs = timeNs;
                        }
                    }
                }
                catch
                {
                    removepcIncomboBox1(thispc.PcName);
                    removepcIncomboBox2(thispc.PcName);
                    foreach (PC pc in connection)
                    {
                        if (string.Equals(pc.PcName, thispc.PcName))
                        {
                            connection.Remove(pc);
                        }
                    }
                }
            }
        }
        delegate void NewPc1(string text);
        delegate void NewPc2(string text);
        delegate void DelPc1(string text);
        delegate void DelPc2(string text);
        delegate void Listbox(string text);
        private void addpcTocomboBox1(string text)
        {
            if (comboBox1.InvokeRequired)
            {
                NewPc1 newPc1 = new NewPc1(addpcTocomboBox1);//子线程调用主线程控件时需要判断控件是否invokerequired
                this.Invoke(newPc1, new object[] { text });
            }
            else
            {
                comboBox1.Items.Add(text);
            }
        }
        private void addpcTocomboBox2(string text)
        {
            if (comboBox2.InvokeRequired)
            {
                NewPc2 newPc2 = new NewPc2(addpcTocomboBox2);
                this.Invoke(newPc2, new object[] { text });
            }
            else
            {
                comboBox2.Items.Add(text);
            }
        }
        private void removepcIncomboBox1(string text)
        {
            if (comboBox1.InvokeRequired)
            {
                DelPc1 delPc1 = new DelPc1(removepcIncomboBox1);
                this.Invoke(delPc1, new object[] { text });
            }
            else
            {
                try
                {
                    comboBox1.Items.Remove(text);
                }
                catch { }
            }
        }
        private void removepcIncomboBox2(string text)
        {
            if (comboBox2.InvokeRequired)
            {
                DelPc2 delPc2 = new DelPc2(removepcIncomboBox2);
                this.Invoke(delPc2, new object[] { text });
            }
            else
            {
                try
                {
                    comboBox2.Items.Remove(text);
                }
                catch { }
            }
        }
        private void record(string text)
        {
            if (listBox1.InvokeRequired)
            {
                Listbox lb = new Listbox(record);
                this.Invoke(lb, new object[] { text });
            }
            else
            {
                listBox1.Items.Add(text);
                if (autorefresh)
                {
                    ListBoxAutoCroll(listBox1);
                }
            }
        }
        private long stamptosecond(long stamp)
        {
            long hour, min, sec;
            sec = stamp % 100;
            min = (stamp % 10000 - sec) / 100;
            hour = (stamp - min * 100 - sec) / 10000;
            return min * 60 + sec;

        }
        public void ListBoxAutoCroll(ListBox lbox)
        {
            lbox.TopIndex = lbox.Items.Count - (int)(lbox.Height / lbox.ItemHeight);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            autorefresh = true;
            listBox1.SetSelected(0, false);
        }
        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < 30; i++)
                lastOffset30[i] = 0;
            average = 0;
            ret = 0;
            top = false;
            listBox1.Items.Clear();
            myCurve.Clear();
        }
        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < 30; i++)
                lastOffset30[i] = 0;
            average = 0;
            ret = 0;
            top = false;
            listBox1.Items.Clear();
            myCurve.Clear();
        }

        private void listBox1_SelectedIndexChanged_1(object sender, EventArgs e)
        {
            try
            {
                if (listBox1.SelectedIndex >= 0)
                    autorefresh = false;
            }
            catch { }
        }

        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void startButton_Click(object sender, EventArgs e)
        {
            try
            {
                PORT = Convert.ToInt32(portTextBox.Text);
                IPAddress ip = IPAddress.Any;
                serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                serverSocket.Bind(new IPEndPoint(ip, PORT));  //绑定IP地址：端口  
                serverSocket.Listen(50);
                Thread th = new Thread(server);
                th.Start(serverSocket);
                startButton.Enabled = false;
                MessageBox.Show("测量服务器已开启");
            }
            catch
            {
                MessageBox.Show("输入端口错误");
            }
        }

        private void zedGraphControl1_Load(object sender, EventArgs e)
        {

        }
    }

    class PC
    {
        
        public PC(){}
        public string PcName;
        public int TimeStamp, TimeNs;
        public long TimeS;
    }
}
