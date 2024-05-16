using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

//dotnet add package System.Drawing.Common --version 8.0.1
#pragma warning disable CA1416, CS8604, CS8618

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
            using var g = Graphics.FromImage(this.Bitmap);
            g.DrawImage(bitmap, new Rectangle(Point.Empty, this.Size));
        }

        public Bmp(string FileName)
        {
            using var bmp = new Bitmap(FileName);
            this.Setup(new Size(bmp.Width, bmp.Height));
            using var g = Graphics.FromImage(this.Bitmap);
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
            GC.SuppressFinalize(this);
        }

        public IEnumerable<Rectangle> Find(Bmp Target, float Tolerance = 0.2f, Rectangle Area = default)
        {
            if (Area == default) Area = new Rectangle(0, 0, this.Size.Width, this.Size.Height);
            int yLimit = Area.Height - Target.Size.Height + 1;
            int xLimit = Area.Width - Target.Size.Width + 1;
            if (yLimit * xLimit <= 0) throw new("Area size needs to be bigger than target's size.");
            bool isEqual(Color a, Color b)
            {
                if (a.A == 0 || b.A == 0) return true;
                if (Tolerance <= 0) return a == b;
                return Math.Abs(a.GetBrightness() - b.GetBrightness()) <= Tolerance;
            }
            bool isMatching(int xOffset, int yOffset)
            {
                for (int y = 0; y < Target.Size.Height; y += 4)
                    for (int x = 0; x < Target.Size.Width; x += 4)
                        if (!isEqual(this.GetPixel(xOffset + x, yOffset + y), Target.GetPixel(x, y)))
                            return false;
                return true;
            }

            for (int y = Area.Y, skipY = 0; y < yLimit + Area.Y; y += skipY > 0 ? Target.Size.Height : 1, skipY = 0)
                for (int x = Area.X, skipX = 0; x < xLimit + Area.X; x += skipX > 0 ? Target.Size.Width : 1, skipX = 0)
                    if (isMatching(x, y))
                    {
                        skipY = 1; skipX = 1;
                        Rectangle result = new (x, y, Target.Size.Width, Target.Size.Height);
                        yield return result;
                    }
        }

        public Rectangle[] FindAll(Bmp Target, float Tolerance = 0.2f, Rectangle Area = default)
        {
            if (Area == default) Area = new Rectangle(0, 0, this.Size.Width, this.Size.Height);
            int xSlices = Math.Min(2, Area.Width / Target.Size.Width);
            int ySlices = Math.Min(2, Area.Height / Target.Size.Height);
            if (xSlices * ySlices <= 0) throw new("Area size needs to be bigger than target's size.");
            if (xSlices < 2 || ySlices < 2) return this.Find(Target, Tolerance, Area).ToArray();
            //TODO: dynamically find 1 to 2 x and y slices
            // Console.WriteLine($"{xSlices}, {ySlices}");

            int sliceHeight = Area.Height / 2 + Target.Size.Height / 2;
            int sliceWidth = Area.Width / 2 + Target.Size.Width / 2;
            List<Task<Rectangle[]>> tasks = new(4);

            for (int y = 0; y < ySlices; y++)
            {
                for (int x = 0; x < xSlices; x++)
                {
                    int yOffset = y * (Area.Height / 2 - Target.Size.Height / 2) + Area.Y;
                    int xOffset = x * (Area.Width / 2 - Target.Size.Width / 2) + Area.X;
                    Rectangle slice = new (xOffset, yOffset, sliceWidth, sliceHeight);
                    tasks.Add(Task.Run(() => this.Find(Target, Tolerance, slice).ToArray()));
                }
            }

            return Task.WhenAll(tasks).Result.SelectMany(x => x).ToArray();
        }

        public void DrawRectangle(Rectangle Rect, Color Color = default, int Thickness = 1)
        {
            Color = Color == default ? Color.Red : Color;
            lock (this.Bitmap)
            {
                using var g = Graphics.FromImage(this.Bitmap);
                using var pen = new Pen(Color, Thickness);
                g.DrawRectangle(pen, Rect);
            }
        }

        public static Bmp CreateFromScreen()
        {
            var bmp = new Bmp(Screen.Resolution);
            bmp.CopyFromScreen();
            return bmp;
        }

        public void CopyFromScreen()
        {
            using var g = Graphics.FromImage(this.Bitmap);
            g.CopyFromScreen(Point.Empty, Point.Empty, Size, CopyPixelOperation.SourceCopy);
        }

        public override string ToString()
        {
            using var bmp = new Bmp(new Bitmap(this.Bitmap, new Size(16, 16)));
            var result = new StringBuilder();
            int binary(int x, int y) => bmp.GetPixel(x, y).GetBrightness() < 0.5f ? 1 : 0;
            for (int y = 0; y < bmp.Size.Height; y++)
            {
                for (int x = 0; x < bmp.Size.Width; x++)
                    result.Append($"{binary(x, y)}, ");
                result.AppendLine();
            }
            return result.ToString().Remove(result.Length - 2, 2);
        }
    }

    public struct Screen
    {

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
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            using var pen = new Pen(color, thickness);
            g.DrawRectangle(pen, Rect);
        }

        private static void UpdateResolution()
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            int width = GetDeviceCaps(hdc, DcFlags.Width);
            int height = GetDeviceCaps(hdc, DcFlags.Height);
            Resolution = new Size(width, height);
            ReleaseDC(IntPtr.Zero, hdc);
        }

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
        private static extern int GetDeviceCaps(IntPtr hDC, DcFlags index);
        private enum DcFlags { Width = 118, Height = 117 }

        [DllImport("User32.dll")]
        private static extern void mouse_event(MouseEvent dwFlags, int dx, int dy);
        private enum MouseEvent { LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010, Move = 0x0001, Absolute = 0x8000 }

        [DllImport("User32.dll")]
        private static extern void keybd_event(ConsoleKey bVk, byte bScan, KeyEvent dwFlags);
        private enum KeyEvent { Down = 0, Up = 2 }
    }

}