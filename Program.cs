using Finder;
using System.Diagnostics;

using var img_a = new Bmp("images/a.png");
using var img_b = new Bmp("images/b.png");

var timeOfAll = new Stopwatch();
timeOfAll.Restart();

var timeByStep = new Stopwatch();
timeByStep.Restart();

img_a.FindAll(img_b, 0.4f, default, (x) =>
{
    img_a.DrawRectangle(x);
    Console.WriteLine($"{x}: {timeByStep.ElapsedMilliseconds}ms");
    timeByStep.Restart();
}).Wait();

Console.WriteLine($"done: {timeOfAll.ElapsedMilliseconds}ms");
timeOfAll.Restart();

img_a.Bitmap.Save("images/c.png");