using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

//dotnet add package System.Drawing.Common --version 6.0.0
#pragma warning disable CA1416, CS8601, CS8602, CS8604, CS8618, CS8625

namespace Finder
{

    public class Bmp : IDisposable
    {
        public Bitmap Bitmap { get; private set; }
        public Size Size { get; private set; }
        private bool Disposed { get; set; }
        private int[] Data { get; set; }

        private GCHandle Handle { get; set; }

        private void Setup(Size size)
        {
            this.Size = size;
            this.Data = new int[size.Width * size.Height];
            this.Handle = GCHandle.Alloc(this.Data, GCHandleType.Pinned);
            this.Bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, this.Handle.AddrOfPinnedObject());
        }

        public Bmp(Size size)
        {
            this.Setup(size);
        }

        public Bmp(Bitmap bitmap)
        {
            this.Setup(new Size(bitmap.Width, bitmap.Height));
            using (var g = Graphics.FromImage(this.Bitmap))
                g.DrawImage(bitmap, new Rectangle(Point.Empty, this.Size));
        }

        public Bmp(string FileName)
        {
            using var bmp = new Bitmap(FileName);
            this.Setup(new Size(bmp.Width, bmp.Height));
            using (var g = Graphics.FromImage(this.Bitmap))
                g.DrawImage(bmp, new Rectangle(Point.Empty, this.Size));
        }

        ~Bmp()
        {
            this.Dispose();
        }

        public void SetPixel(int X, int Y, Color color)
        {
            int index = X + (Y * this.Size.Width);
            this.Data[index] = color.ToArgb();
        }

        public Color GetPixel(int X, int Y)
        {
            int index = X + (Y * this.Size.Width);
            return Color.FromArgb(this.Data[index]);
        }

        public void Dispose()
        {
            if (this.Disposed) return;
            this.Bitmap.Dispose();
            this.Handle.Free();
            this.Size = Size.Empty;
            this.Data = Array.Empty<int>();
            this.Disposed = true;
        }

        private Rectangle Find(Bmp Target, Rectangle Area = default, float Tolerance = 0, CancellationToken Token = default)
        {
            if (Area == default) Area = new Rectangle(0, 0, this.Size.Width, this.Size.Height);
            int yLimit = Area.Height - Target.Size.Height + 1;
            int xLimit = Area.Width - Target.Size.Width + 1;
            if (yLimit * xLimit <= 0) throw new("Area size needs to be bigger than target's size.");
            var Distance = (Color a, Color b) => (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 765.0f;

            for (int y1 = Area.Y; y1 < yLimit + Area.Y; y1++)
                for (int x1 = Area.X; x1 < xLimit + Area.X; x1++)
                {
                    bool found = false;
                    for (int y2 = 0; y2 < Target.Size.Height; y2++)
                    {
                        for (int x2 = 0; x2 < Target.Size.Width; x2++)
                        {
                            if (Token.IsCancellationRequested) return Rectangle.Empty;
                            var aPixel = this.GetPixel(x1 + x2, y1 + y2);
                            var bPixel = Target.GetPixel(x2, y2);
                            if (aPixel.A == 0 || bPixel.A == 0) continue;
                            if (Tolerance <= 0) found = aPixel == bPixel;
                            else found = Distance(aPixel, bPixel) <= Tolerance;
                            if (!found) break;
                        }
                        if (!found) break;
                    }
                    if (found) return new Rectangle(x1, y1, Target.Size.Width, Target.Size.Height);
                }
            return Rectangle.Empty;
        }

        private Task<Rectangle> FindAsync(Bmp Target, Rectangle Area = default, float Tolerance = 0, CancellationToken Token = default)
        {
            return Task.Run<Rectangle>(() => this.Find(Target, Area, Tolerance, Token), Token);
        }

        public Rectangle Find(Bmp Target, Rectangle Area = default, float Tolerance = 0)
        {
            using var cts = new CancellationTokenSource();
            if (Area == default) Area = new Rectangle(0, 0, this.Size.Width, this.Size.Height);
            int slices = (Area.Width / Target.Size.Width) * (Area.Height / Target.Size.Height);
            if (slices <= 0) throw new("Area size needs to be bigger than target's size.");
            else if (slices < 4) return this.Find(Target, Area, Tolerance, cts.Token);

            var tasks = new List<Task<Rectangle>>();
            var slice = new Size(Area.Width / 2, Area.Height / 2);
            int width = slice.Width + (Target.Size.Width / 2);
            int height = slice.Height + (Target.Size.Height / 2);

            for (int y = 0; y < 2; y++)
            {
                int yPos = y * (slice.Height - Target.Size.Height / 2) + Area.Y;
                for (int x = 0; x < 2; x++)
                {
                    int xPos = x * (slice.Width - Target.Size.Width / 2) + Area.X;
                    var rect = new Rectangle(xPos, yPos, width, height);
                    tasks.Add(this.FindAsync(Target, rect, Tolerance, cts.Token));
                }
            }

            return WaitAndAvoid(tasks, Rectangle.Empty, cts);
        }

        public Task<Rectangle> FindAsync(Bmp Target, Rectangle Area = default, float Tolerance = 0)
        {
            return Task.Run<Rectangle>(() => this.Find(Target, Area, Tolerance));
        }

        private static T WaitAndAvoid<T>(List<Task<T>> Tasks, T Avoid = default, CancellationTokenSource TokenSource = default)
        {
            T result = Avoid;
            while (result.Equals(Avoid) && Tasks.Count > 0)
            {
                int faster = Task.WaitAny(Tasks.ToArray(), 1000);
                if (faster < 0) break;
                result = Tasks[faster].Result;
                Tasks.RemoveAt(faster);
            }
            TokenSource.Cancel();
            return result;
        }

        public void DrawRectangle(Rectangle Rect, Color Color = default, int Thickness = 1)
        {
            Color = Color == default ? Color.Red : Color;
            using (var g = Graphics.FromImage(this.Bitmap))
            using (var pen = new Pen(Color, Thickness))
                g.DrawRectangle(pen, Rect);
        }

        public void FromScreen()
        {
            using (var g = Graphics.FromImage(this.Bitmap))
                g.CopyFromScreen(Point.Empty, Point.Empty, Size, CopyPixelOperation.SourceCopy);
        }

        public override string ToString()
        {
            using var bmp = new Bitmap(this.Bitmap, new Size(16, 16));
            var result = new StringBuilder();
            var binary = (int x, int y) => bmp.GetPixel(x, y).GetBrightness() < 0.5f ? 1 : 0;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    result.Append($"{binary(x, y)}, ");
            return result.ToString().Remove(result.Length - 2, 2);
        }
    }

    public struct Screen
    {
        [DllImport("User32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("User32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("User32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("User32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("User32.dll")]
        private static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hDC, dcFlags index);
        private enum dcFlags { Width = 118, Height = 117 }

        [DllImport("User32.dll")]
        private static extern void mouse_event(MouseEvent dwFlags, int dx, int dy);
        private enum MouseEvent { LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010, Move = 0x0001, Absolute = 0x8000 }

        [DllImport("User32.dll")]
        private static extern void keybd_event(ConsoleKey bVk, byte bScan, KeyEvent dwFlags);
        private enum KeyEvent { Down = 0, Up = 2 }

        public static Size Resolution { get; private set; }

        public static Point Cursor
        {
            get { GetCursorPos(out Point point); return point; }
            set => SetCursorPos(value.X, value.Y);
        }

        static Screen()
        {
            SetProcessDPIAware();
            UpdateResolution();
        }

        public static void Press(ConsoleKey Key)
        {
            keybd_event(Key, 0, KeyEvent.Down);
            keybd_event(Key, 0, KeyEvent.Up);
        }

        public static void Press(string Keys)
        {
            foreach (char key in Keys)
            {
                Enum.TryParse(typeof(ConsoleKey), key.ToString(), true, out var cKey);
                Press(cKey == null ? ConsoleKey.Spacebar : (ConsoleKey)cKey);
            }
        }

        public static void Click()
        {
            mouse_event(MouseEvent.LeftDown, 0, 0);
            mouse_event(MouseEvent.LeftUp, 0, 0);
        }

        public static void DrawRectangle(Rectangle Rect, Color color = default, int thickness = 1)
        {
            color = color == default ? Color.Red : color;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            using (var pen = new Pen(color, thickness))
                g.DrawRectangle(pen, Rect);
        }

        private static void UpdateResolution()
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            int width = GetDeviceCaps(hdc, dcFlags.Width);
            int height = GetDeviceCaps(hdc, dcFlags.Height);
            Resolution = new Size(width, height);
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

}