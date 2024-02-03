using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

//dotnet add package System.Drawing.Common --version 8.0.1
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
        public bool Break = false;

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

        public IEnumerable<Rectangle> Find(Bmp Target, float Tolerance = 0, Rectangle Area = default, Action<(Rectangle rect, int ThreadId)> OnFound = null)
        {
            if (Area == default) Area = new Rectangle(0, 0, this.Size.Width, this.Size.Height);
            int yLimit = Area.Height - Target.Size.Height + 1;
            int xLimit = Area.Width - Target.Size.Width + 1;
            if (yLimit * xLimit <= 0) throw new("Area size needs to be bigger than target's size.");
            var isEqual = (Color a, Color b) =>
            {
                if (a.A == 0 || b.A == 0) return true;
                if (Tolerance <= 0) return a == b;
                return Math.Abs(a.GetBrightness() - b.GetBrightness()) <= Tolerance;
            };

            bool skipLine, found;
            for (int y1 = Area.Y; y1 < yLimit + Area.Y; y1 += skipLine ? Target.Size.Height : 1)
            {
                skipLine = false;
                for (int x1 = Area.X; x1 < xLimit + Area.X; x1 += found ? Target.Size.Width : 1)
                {
                    found = false;
                    for (int y2 = 0; y2 < Target.Size.Height; y2 += 4)
                    {
                        for (int x2 = 0; x2 < Target.Size.Width; x2 += 4)
                        {
                            if (this.Break) yield break;
                            found = isEqual(this.GetPixel(x1 + x2, y1 + y2), Target.GetPixel(x2, y2));
                            if (!found) break;
                        }
                        if (!found) break;
                    }
                    if (found)
                    {
                        skipLine = found;
                        var result = new Rectangle(x1, y1, Target.Size.Width, Target.Size.Height);
                        OnFound?.Invoke((result, Thread.CurrentThread.ManagedThreadId));
                        yield return result;
                    }
                }
            }

            yield break;
        }

        public void FindAll(Bmp Target, float Tolerance = 0, Rectangle Area = default, Action<(Rectangle rect, int ThreadId)> OnFound = null)
        {
            this.Break = false;

            if (Area == default) Area = new Rectangle(0, 0, this.Size.Width, this.Size.Height);
            int maxSlices = (Area.Width / Target.Size.Width) * (Area.Height / Target.Size.Height);
            if (maxSlices <= 0) throw new("Area size needs to be bigger than target's size.");
            if (maxSlices < 4) this.Find(Target, Tolerance, Area, OnFound);

            int sliceHeight = Area.Height / 2 + Target.Size.Height / 2;
            int sliceWidth = Area.Width / 2 + Target.Size.Width / 2;

            var tasks = new List<Task>(4);
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    int yOffset = y * (Area.Height / 2 - Target.Size.Height / 2) + Area.Y;
                    int xOffset = x * (Area.Width / 2 - Target.Size.Width / 2) + Area.X;
                    var slice = new Rectangle(xOffset, yOffset, sliceWidth, sliceHeight);
                    tasks.Add(Task.Run(() => this.Find(Target, Tolerance, slice, OnFound).Count()));
                }
            }
            Task.WhenAll(tasks).Wait();
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