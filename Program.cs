using Finder;

var img_a = new Bmp("images/a.png");
var img_b = new Bmp("images/b.png");

var finder = img_a.FindAsync(img_b, default, 0.2f);

img_a.DrawRectangle(finder.Result);
img_a.Bitmap.Save("images/c.png");
