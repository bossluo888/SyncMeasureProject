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
        long curAverSum = 0;
        long average = 0;
        int averNum = 0;
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
            myPane.YAxis.Scale.MaxAuto = true;   // 自动调整Y轴最大值
            myPane.YAxis.Scale.MinAuto = true;   // 自动调整Y轴最小值
            myPane.YAxis.Scale.MinorStepAuto = true;   // 自动调整Y轴小步长
            myPane.YAxis.Scale.MajorStepAuto = true;   // 自动调整Y轴主步长
            myPane.XAxis.Scale.MaxAuto = true;   //自动调整X轴最大值
            myPane.XAxis.Scale.MinAuto = true;   //自动调整X轴最小值
            myPane.XAxis.Scale.MinorStep = 5;   // X轴的小步长
            myPane.XAxis.Scale.MajorStep = 5;  // X轴的主步长
            myPane.Title.FontSpec.Size = 22;  // 标题字体大小
            myPane.XAxis.Title.FontSpec.Size = 16;  // X轴标签字体大小
            myPane.YAxis.Title.FontSpec.Size = 16;  // Y轴标签字体大小
            System.Timers.Timer timer = new System.Timers.Timer(1000);  // 每隔1秒更新一次界面
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
                string sign, strSec, strNSec;
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
                // 如果两者的时间戳相同(取决于定时器)才显示数据，精确到秒
                if (timeStamp1 == timeStamp2)
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
                        sign = " ";
                        absOffset = offset;
                    }
                    else
                    {
                        sign = "-";
                        absOffset = -offset;
                    }
                    strSec = sign + (absOffset / 1000000000).ToString();
                    strNSec = (absOffset % 1000000000).ToString("D9");
                    pointPairList.Add((stamptosecond(timeStamp1)), (double)offset / 1000);  // 增加数据点，注意需转为double，因为可能出现小数
                    this.zedGraphControl1.AxisChange();
                    this.zedGraphControl1.Invalidate();   
                    //this.zedGraphControl1.Refresh();
                    if (pointPairList.Count >= 50)   // 保持50个数据点
                    {
                        pointPairList.RemoveAt(0);
                    }
                    /**
                     * 将误差显示为秒，毫秒，微妙，纳秒的形式
                     */
                    textBox1.Text = strSec;
                    textBox2.Text = strNSec.Substring(0, 3);
                    textBox3.Text = strNSec.Substring(3, 3);
                    textBox4.Text = strNSec.Substring(6, 3);
                    if (offset != preOffset)
                    {
                        /**
                         * 将误差显示为秒的形式，并保持记录历史数据
                         */
                        record(timeStamp1.ToString("D6") + ": " + "offset: " + strSec + "." + strNSec + " s");
                        preOffset = offset;
                        /**
                         * 计算最近30个数据的平均值
                         */
                        if (averNum == 30)
                        {
                            averNum = 0;
                            top = true;
                        }
                        if (averNum < 30)
                        {
                            curAverSum = curAverSum - lastOffset30[averNum] + offset;
                            lastOffset30[averNum] = offset;
                        }
                        if (top)
                        {
                            average = (long)Math.Round((double)(curAverSum / 30), 0);
                        }
                        if (!top)
                        {
                            average = (long)Math.Round((double)(curAverSum / (averNum + 1)), 0);                            
                        }
                        averNum += 1;
                        /**
                         * 显示误差平均值，显示为秒的形式
                         */
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
            portTextBox.Text = "8888";  // 默认端口号为8888
            myCurve = zedGraphControl1.GraphPane.AddCurve("Offset between two PCs", pointPairList, Color.DarkGreen, SymbolType.Circle);  // 开始添加折线
            myCurve.Line.Width = 2.0F;  // 设置折线的线条宽度
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
            byte[] result = new byte[128];   // 接收缓存
            PC thispc = new PC();
            connection.Add(thispc);
            Socket myClientSocket = (Socket)clientSocket;
            thispc.PcName = myClientSocket.RemoteEndPoint.ToString();//将客户端ip地址和端口号作为终端名
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
                NewPc1 newPc1 = new NewPc1(addpcTocomboBox1); // 子线程调用主线程控件时需要判断控件是否invokerequired，线程安全考虑
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
            // 防止没有数据而点刷新时出现的数组溢出异常
            if (listBox1.Items.Count > 0)
            {
                listBox1.SetSelected(0, false);
            }          
        }
        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < 30; i++)
                lastOffset30[i] = 0;
            average = 0;
            averNum = 0;
            top = false;
            listBox1.Items.Clear();
            myCurve.Clear();
        }
        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < 30; i++)
                lastOffset30[i] = 0;
            average = 0;
            averNum = 0;
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
                MessageBox.Show("同步测量服务器已开启");
            }
            catch
            {
                MessageBox.Show("输入端口错误");
            }
        }

    }

    class PC
    {  
        public string PcName;
        public int TimeStamp, TimeNs;
        public long TimeS;
    }
}
