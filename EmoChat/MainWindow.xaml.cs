using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

// 追加分
using Microsoft.Win32;
using System.IO;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Threading;

using OpenCvSharp;
using OpenCvSharp.Extensions;

using Newtonsoft.Json.Linq;
using Microsoft.ProjectOxford.Emotion;
using Microsoft.ProjectOxford.Emotion.Contract;

namespace EmoChat
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    // デリゲート関数
    public delegate void writeTextDelegate(string str);
    public delegate void writeImageDelegate(Bitmap bitmap);
    public delegate string readTextDelegate();

    public partial class MainWindow : Window
    {
        CvCapture camera = null;
        IplImage face = null;
        string emotionL = "Neutral";
        string emotionR = "Neutral";
        string faceL = "faceL.jpg";
        string faceR = "faceR.jpg";
        string srcimg = "fd7b29ec-s.jpg";
        int encode = 932;   // Shift-JIS
        Dictionary<string, IplImage> stamps = null;
        DispatcherTimer dispatcherTimer;

        public MainWindow()
        {
            InitializeComponent();

            // カメラを設定
            camera = Cv.CreateCameraCapture(0);
            //face = new IplImage();

            // スタンプ画像を生成
            stamps = new Dictionary<string, IplImage>();
            IplImage sheet = Cv.LoadImage(srcimg);
            Dictionary<string, List<int>> emotions = new Dictionary<string, List<int>>();
            emotions.Add("Anger", new List<int>() { 0, 0, 120, 120 });
            emotions.Add("Contempt", new List<int>() { 140, 140, 120, 120 });
            emotions.Add("Disgust", new List<int>() { 140, 140, 120, 120 });
            emotions.Add("Fear", new List<int>() { 140, 0, 120, 120 });
            emotions.Add("Happiness", new List<int>() { 280, 140, 120, 120 });
            emotions.Add("Neutral", new List<int>() { 140, 280, 120, 120 });
            emotions.Add("Sadness", new List<int>() { 280, 0, 120, 120 });
            emotions.Add("Surprise", new List<int>() { 0, 280, 120, 120 });
            foreach (var key in emotions.Keys)
            {
                IplImage trimmed = new IplImage();
                IplImage stamp = new IplImage((int)image1.Width, (int)image1.Height, sheet.Depth, sheet.NChannels);
                trimmed = trimming(sheet, emotions[key][0], emotions[key][1], emotions[key][2], emotions[key][3]);
                Cv.Resize(trimmed, stamp, Interpolation.NearestNeighbor);
                stamps.Add(key, stamp);
            }

            // タイマーを設定
            dispatcherTimer = new DispatcherTimer(DispatcherPriority.Normal);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 30);
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Start();

            // 入力欄にフォーカス
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(textBox), textBox);

            // TCP待ち受けスレッド生成
            Thread t = new Thread(new ThreadStart(ListenData));
            t.IsBackground = true;
            t.Start();
        }

        // ポート番号取得
        public string readPortNum()
        {
            return textBox2.Text;
        }

        // テキストデータ書き込み
        public void writeTextData(string str)
        {
            richTextBox.AppendText(str);
            richTextBox.ScrollToEnd();
        }

        // 画像データ書き込み
        public void writeImageData(Bitmap bitmap)
        {
            Clipboard.Clear();
            Clipboard.SetDataObject(bitmap);
            richTextBox.CaretPosition = richTextBox.Document.ContentEnd;
            richTextBox.Paste();
            richTextBox.ScrollToEnd();
        }

        // TCP待ち受け
        public async void ListenData()
        {
            // 待ち受けアドレス、ポートの設定
            string localhost = Dns.GetHostName();
            string str_ipad = null;
            IPAddress[] adrList = Dns.GetHostAddresses(localhost);
            foreach (IPAddress address in adrList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    str_ipad = address.ToString();
                    break;
                }
            }
            IPAddress ipad = IPAddress.Parse(str_ipad);
            string str_port = (string)textBox2.Dispatcher.Invoke(new readTextDelegate(readPortNum));
            Int32 port = Int32.Parse(str_port);
            TcpListener tl = new TcpListener(ipad, port);
            tl.Start();

            // メッセージの処理
            while (true)
            {
                TcpClient tc = tl.AcceptTcpClient();
                NetworkStream ns = tc.GetStream();

                // 受信したデータがなくなるまで繰り返す
                var typ = new byte[1];
                var len = new byte[4];
                while (ns.Read(typ, 0, typ.Length) != 0)
                {
                    ns.Read(len, 0, len.Length);
                    int num = BitConverter.ToInt32(len, 0);
                    byte[] data;

                    switch (typ[0])
                    {
                        case 0: // テキストデータの処理
                            data = new byte[num];
                            ns.Read(data, 0, data.Length);
                            var str = Encoding.GetEncoding(encode).GetString(data);
                            Dispatcher.Invoke(new writeTextDelegate(writeTextData), new object[] { str });
                            break;
                        case 1: // 画像データの処理
                            int readsize = 0;
                            data = new byte[num];
                            while (readsize < num)
                            {
                                readsize += ns.Read(data, readsize, num - readsize);
                            }
                            BitmapImage bitmapImage = LoadImage(data);
                            Bitmap bitmap = BitmapImage2Bitmap(bitmapImage);
                            //Dispatcher.Invoke(new writeImageDelegate(writeImageData), new object[] { bitmap });
                            bitmap.Save(faceR, ImageFormat.Jpeg);
                            break;
                        default:
                            break;
                    }
                }

                if (File.Exists(faceR))
                {
                    // 感情情報を取得
                    Emotion[] response = await UploadAndDetectEmotions(faceR);
                    File.Delete(faceR);
                    if (response == null) {
                        //MessageBox.Show("Error occured!");
                    }
                    else
                    {
                        Dictionary<string, float> scores = new Dictionary<string, float>();
                        foreach (Emotion emo in response)
                        {
                            scores.Add("Anger", emo.Scores.Anger);
                            scores.Add("Contempt", emo.Scores.Contempt);
                            scores.Add("Disgust", emo.Scores.Disgust);
                            scores.Add("Fear", emo.Scores.Fear);
                            scores.Add("Happiness", emo.Scores.Happiness);
                            scores.Add("Neutral", emo.Scores.Neutral);
                            scores.Add("Sadness", emo.Scores.Sadness);
                            scores.Add("Surprise", emo.Scores.Surprise);
                        }
                        emotionR = scores.OrderByDescending((x) => x.Value).First().Key;
                    }

                    // 行末にスタンプを挿入
                    IplImage stamp = stamps[emotionR];
                    IplImage resized = new IplImage(15, 15, stamp.Depth, stamp.NChannels);
                    Cv.Resize(stamp, resized);
                    Bitmap bmp = BitmapConverter.ToBitmap(resized);
                    Dispatcher.Invoke(new writeImageDelegate(writeImageData), new object[] { bmp });

                    // 改行
                    Dispatcher.Invoke(new writeTextDelegate(writeTextData), new object[] { "\n" });
                }

                tc.Close();
            }

            tl.Stop();
        }

        // byte配列からBitmapImageに変換
        private static BitmapImage LoadImage(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0) return null;
            var image = new BitmapImage();
            using (var mem = new MemoryStream(imageData))
            {
                mem.Position = 0;
                image.BeginInit();
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = null;
                image.StreamSource = mem;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }

        // BitmapImageからBitmapに変換
        private Bitmap BitmapImage2Bitmap(BitmapImage bitmapImage)
        {
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                enc.Save(outStream);
                Bitmap bitmap = new Bitmap(outStream);

                return new Bitmap(bitmap);
            }
        }

        // 一定時間経過ごとに実行
        void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            // カメラから画像を取得
            IplImage frame = Cv.QueryFrame(camera);

            // 640x480の画像から320x240を切り出す
            double w = 640, h = 480;
            Cv.SetCaptureProperty(camera, CaptureProperty.FrameWidth, w);
            Cv.SetCaptureProperty(camera, CaptureProperty.FrameHeight, h);
            face = trimming(frame, (int)w / 4, (int)h / 4, (int)image.Width, (int)image.Height);

            // フレーム画像をコントロールに貼り付け
            image.Source = WriteableBitmapConverter.ToWriteableBitmap(face);

            // 感情認識結果をオーバーレイ
            IplImage stamp = stamps[emotionL];
            image1.Source = WriteableBitmapConverter.ToWriteableBitmap(stamp);
        }

        // 画像のトリミング
        private IplImage trimming(IplImage src, int x, int y, int width, int height)
        {
            IplImage dest = new IplImage(width, height, src.Depth, src.NChannels);
            Cv.SetImageROI(src, new CvRect(x, y, width, height));
            dest = Cv.CloneImage(src);
            Cv.ResetImageROI(src);
            return dest;
        }

        // MessageでEnterキーを押したときにSendボタンと同様の動作を行う
        private void textBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                button_Click(sender, e);
            }
        }

        // Sendボタン押下時
        private async void button_Click(object sender, RoutedEventArgs e)
        {
            // TCPクライアントの設定
            string HostName = textBox1.Text;
            int port = Int32.Parse(textBox2.Text);
            TcpClient tc = new TcpClient(HostName, port);
            NetworkStream ns = tc.GetStream();

            // Messageの送信
            if (textBox.Text != "")
            {
                // 送信データのタイプ（テキスト）
                var typ = new byte[1];
                typ[0] = 0x0000;

                // 送信するデータ
                string str = textBox3.Text + " > " + textBox.Text + " ";
                textBox.Clear();
                var mesg = Encoding.GetEncoding(encode).GetBytes(str);

                // データの長さ
                var len = BitConverter.GetBytes(mesg.Length);

                // タイプとデータ本体を結合して送信
                var bary = typ.Concat(len).Concat(mesg).ToArray();
                ns.Write(bary, 0, bary.Length);

                // テキストボックスに書き出し
                writeTextData(str);
            }

            // 画像の送信
            if (face != null)
            {
                // 送信データのタイプ（画像）
                var typ = new byte[1];
                typ[0] = 0x0001;

                // 画像データの内容をコピー
                Bitmap bitmap = BitmapConverter.ToBitmap(face);
                bitmap.Save(faceL, ImageFormat.Jpeg);
                MemoryStream ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Bmp);
                var img = ms.GetBuffer();

                // データの長さ
                var len = BitConverter.GetBytes(img.Length);

                // タイプと長さとデータ本体を連結して送信
                var bary = typ.Concat(len).Concat(img).ToArray();
                ns.Write(bary, 0, bary.Length);
            }
            ns.Close();
            tc.Close();

            if (File.Exists(faceL))
            {
                // 感情情報を取得
                Emotion[] response = await UploadAndDetectEmotions(faceL);
                File.Delete(faceL);
                if (response == null) {
                    //MessageBox.Show("Error occured!");
                }
                else
                {
                    Dictionary<string, float> scores = new Dictionary<string, float>();
                    foreach (Emotion emo in response)
                    {
                        scores.Add("Anger", emo.Scores.Anger);
                        scores.Add("Contempt", emo.Scores.Contempt);
                        scores.Add("Disgust", emo.Scores.Disgust);
                        scores.Add("Fear", emo.Scores.Fear);
                        scores.Add("Happiness", emo.Scores.Happiness);
                        scores.Add("Neutral", emo.Scores.Neutral);
                        scores.Add("Sadness", emo.Scores.Sadness);
                        scores.Add("Surprise", emo.Scores.Surprise);
                    }
                    emotionL = scores.OrderByDescending((x) => x.Value).First().Key;
                }

                // 行末にスタンプを挿入
                IplImage stamp = stamps[emotionL];
                IplImage resized = new IplImage(15, 15, stamp.Depth, stamp.NChannels);
                Cv.Resize(stamp, resized);
                Bitmap bmp = BitmapConverter.ToBitmap(resized);
                writeImageData(bmp);

                // 改行
                richTextBox.AppendText("\n");
                richTextBox.ScrollToEnd();
            }
        }

        // HTTPクライアント自作の場合（未完成）
        private async Task<HttpResponseMessage> uploadImage(string file)
        {
            using (var client = new HttpClient())
            {
                // HTTPクライアントの作成
                client.BaseAddress = new Uri("https://api.projectoxford.ai/emotion/v1.0/recognize");
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", "your_key");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                // 送信するデータ（画像）のセット
                HttpContent content = new StreamContent(File.OpenRead(file));
                content.Headers.ContentType = new MediaTypeWithQualityHeaderValue("application/octet-stream");
                richTextBox.AppendText(client.ToString());

                // リクエストを送信（受信まで待機）
                return await client.PostAsync("https://api.projectoxford.ai/emotion/v1.0/recognize", content);
            }
        }

        // 公式のライブラリを利用する場合
        private async Task<Emotion[]> UploadAndDetectEmotions(string imageFilePath)
        {
            string subscriptionKey = "your_key";
            EmotionServiceClient emotionServiceClient = new EmotionServiceClient(subscriptionKey);
            try
            {
                Emotion[] emotionResult;
                using (Stream imageFileStream = File.OpenRead(imageFilePath))
                {
                    // Detect the emotions in the URL
                    emotionResult = await emotionServiceClient.RecognizeAsync(imageFileStream);
                    return emotionResult;
                }
            }
            catch (Exception exception)
            {
                //MessageBox.Show(exception.ToString());
                return null;
            }
        }
    }
}
