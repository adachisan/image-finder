using Finder;
using System.Diagnostics;
using System.Drawing;

using var img_a = new Bmp("images/a.png");
using var img_b = new Bmp("images/b.png");

var metrics = new List<(Rectangle, long)>();
var timeOfAll = new Stopwatch();
var timeByStep = new Stopwatch();

timeOfAll.Restart();
timeByStep.Restart();

foreach (var x in img_a.FindAll(img_b, 0.4f))
{
    metrics.Add((x, timeByStep.ElapsedMilliseconds));
    timeByStep.Restart();
}

Console.WriteLine($"done in {timeOfAll.ElapsedMilliseconds}ms");

metrics.ForEach((x) =>
{
    img_a.DrawRectangle(x.Item1);
    Console.WriteLine($"{x.Item1}: {x.Item2}ms");
});

img_a.Bitmap.Save("images/c.png");