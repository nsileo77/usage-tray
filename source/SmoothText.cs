using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Media=System.Windows.Media;
using Imaging=System.Windows.Media.Imaging;
using Wpf=System.Windows;

// Native WPF/DirectWrite text on transparent pixels; no bitmap scaling or RGB fringes.
public sealed class SmoothText : IDisposable {
 readonly Dictionary<string,Bitmap> cache=new Dictionary<string,Bitmap>();
 static readonly Media.Typeface Face=new Media.Typeface(new Media.FontFamily("Segoe UI"),Wpf.FontStyles.Normal,Wpf.FontWeights.Normal,Wpf.FontStretches.Normal);
 static Media.FormattedText Format(string text,double pixels){return new Media.FormattedText(text??"",CultureInfo.InvariantCulture,Wpf.FlowDirection.LeftToRight,Face,pixels,Media.Brushes.White,null,Media.TextFormattingMode.Display,1.0);}
 public static double MeasureWidth(string text,double pixels){return Format(text,pixels).WidthIncludingTrailingWhitespace;}
 public static double MeasureHeight(string text,double pixels){return Format(text,pixels).Height;}
 public static string Metrics(string text,double pixels){var f=Format(text,pixels);return "font="+pixels+", height="+f.Height+", baseline="+f.Baseline;}
 public Bitmap Get(string text,int width,int height,double pixels,bool right,bool center){
  string key=text+"|"+width+"|"+height+"|"+pixels.ToString(CultureInfo.InvariantCulture)+"|"+right+"|"+center;
  Bitmap found;if(cache.TryGetValue(key,out found))return found;
  if(cache.Count>=32)Clear();
  var visual=new Media.DrawingVisual();
  Media.TextOptions.SetTextFormattingMode(visual,Media.TextFormattingMode.Display);
  Media.TextOptions.SetTextRenderingMode(visual,Media.TextRenderingMode.Grayscale);
  Media.TextOptions.SetTextHintingMode(visual,Media.TextHintingMode.Fixed);
  var formatted=Format(text,pixels);
  formatted.MaxTextWidth=width;formatted.MaxLineCount=1;formatted.Trimming=Wpf.TextTrimming.CharacterEllipsis;
  formatted.TextAlignment=center?Wpf.TextAlignment.Center:right?Wpf.TextAlignment.Right:Wpf.TextAlignment.Left;
  using(var drawing=visual.RenderOpen()){
   drawing.PushClip(new Media.RectangleGeometry(new Wpf.Rect(0,0,width,height)));
   drawing.DrawText(formatted,new Wpf.Point(0,Math.Floor((height-formatted.Height)/2)));
   drawing.Pop();
  }
  var raster=new Imaging.RenderTargetBitmap(width,height,96,96,Media.PixelFormats.Pbgra32);raster.Render(visual);
  int stride=width*4;var bytes=new byte[stride*height];raster.CopyPixels(bytes,stride,0);
  var bitmap=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
  var data=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
  try{for(int row=0;row<height;row++)Marshal.Copy(bytes,row*stride,IntPtr.Add(data.Scan0,row*data.Stride),stride);}finally{bitmap.UnlockBits(data);}
  cache[key]=bitmap;return bitmap;
 }
 void Clear(){foreach(var bitmap in cache.Values)bitmap.Dispose();cache.Clear();}
 public void Dispose(){Clear();}
}



