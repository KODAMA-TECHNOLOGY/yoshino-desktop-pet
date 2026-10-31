using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YoshinoPet {
public sealed class SpriteArt {
    public const int FrameWidth=576,FrameHeight=624;
    public readonly BitmapSource[][] Rows=new BitmapSource[11][];
    public BitmapSource[] Keys;public bool HighResolution;
    static readonly int[] Counts={6,8,8,4,5,8,6,6,6,8,8};
    static BitmapImage Read(string path) {var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(Path.GetFullPath(path));b.EndInit();b.Freeze();return b;}
    public static SpriteArt Load() {
        var art=new SpriteArt();string folder=Path.Combine(Paths.Base,"assets","hq");string[] names={"face.png","motion.png","mood.png"};
        int exists=names.Count(n=>File.Exists(Path.Combine(folder,n)));
        if(exists==0) {
            var bmp=Read(Paths.Atlas);if(bmp.PixelWidth!=1536||bmp.PixelHeight!=2288)throw new Exception("兼容精灵图尺寸错误。");
            for(int r=0;r<11;r++){art.Rows[r]=new BitmapSource[Counts[r]];for(int c=0;c<Counts[r];c++){var b=new CroppedBitmap(bmp,new Int32Rect(c*192,r*208,192,208));b.Freeze();art.Rows[r][c]=b;}}
            return art;
        }
        if(exists!=names.Length)throw new Exception("高清素材不完整，请完整解压新版软件。");
        var sources=new List<BitmapSource>();var bounds=new List<Int32Rect>();
        foreach(string name in names) {
            var bmp=Read(Path.Combine(folder,name));int w=bmp.PixelWidth/2,h=bmp.PixelHeight/2;
            if(w<480||h<480)throw new Exception("高清动作单元小于 480 像素："+name);
            for(int i=0;i<4;i++) {
                var cell=new CroppedBitmap(bmp,new Int32Rect((i%2)*w,(i/2)*h,w,h));cell.Freeze();var frame=CleanAlpha(cell);
                var rect=AlphaBounds(frame);if(rect.Width<100||rect.Height<200)throw new Exception("高清动作为空或不完整："+name);
                sources.Add(frame);bounds.Add(rect);
            }
        }
        double scale=Math.Min((FrameWidth-44.0)/bounds.Max(b=>b.Width),(FrameHeight-48.0)/bounds.Max(b=>b.Height));
        art.Keys=new BitmapSource[12];
        for(int i=0;i<12;i++) {
            var crop=new CroppedBitmap(sources[i],bounds[i]);crop.Freeze();double w=bounds[i].Width*scale,h=bounds[i].Height*scale;
            double baseline=FrameHeight-20-(i==7?16:0);
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(crop,new Rect((FrameWidth-w)/2,baseline-h,w,h));
            var target=new RenderTargetBitmap(FrameWidth,FrameHeight,96,96,PixelFormats.Pbgra32);target.Render(visual);target.Freeze();art.Keys[i]=target;
        }
        // High resolution key drawings retain all nine state slots; small in-between motions are rendered by WPF.
        int[][] mapping={new[]{0,0,1,2,1,0},Repeat(4,8),Repeat(5,8),Repeat(6,4),Repeat(7,5),Repeat(9,8),Repeat(8,6),Repeat(3,6),Repeat(10,6),Repeat(0,8),Repeat(0,8)};
        for(int r=0;r<11;r++)art.Rows[r]=mapping[r].Select(i=>art.Keys[i]).ToArray();
        art.HighResolution=true;return art;
    }
    static int[] Repeat(int value,int count) {return Enumerable.Repeat(value,count).ToArray();}
    internal static BitmapSource CleanAlpha(BitmapSource image) {
        // Technical sprite import: discard disconnected matte debris and a one-pixel extraction fringe.
        // The generated RGB drawing is retained; only the transparency mask is normalized.
        int w=image.PixelWidth,h=image.PixelHeight,n=w*h;var source=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var data=new byte[n*4];source.CopyPixels(data,w*4,0);
        var solid=new bool[n];for(int i=0;i<n;i++){int j=i*4;bool key=data[j+2]>180&&data[j]>170&&data[j+1]<120&&data[j+2]-data[j+1]>95&&data[j]-data[j+1]>80;if(key)data[j+3]=0;solid[i]=data[j+3]>=128;}
        var label=new int[n];var queue=new int[n];int next=0,biggest=0,biggestSize=0;
        for(int root=0;root<n;root++)if(solid[root]&&label[root]==0) {
            next++;int head=0,tail=0;queue[tail++]=root;label[root]=next;
            while(head<tail){int p=queue[head++],x=p%w,y=p/w;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx<0||xx>=w||yy<0||yy>=h)continue;int k=yy*w+xx;if(solid[k]&&label[k]==0){label[k]=next;queue[tail++]=k;}}}
            if(tail>biggestSize){biggestSize=tail;biggest=next;}
        }
        var alpha=new byte[n];for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=y*w+x;bool near=false;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx>=0&&xx<w&&yy>=0&&yy<h&&label[yy*w+xx]==biggest)near=true;}if(near)alpha[p]=data[p*4+3];}
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){byte a=alpha[y*w+x];for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;a=(byte)Math.Min(a,xx<0||xx>=w||yy<0||yy>=h?0:alpha[yy*w+xx]);}data[(y*w+x)*4+3]=a;}
        var result=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,data,w*4);result.Freeze();return result;
    }
    public static Int32Rect AlphaBounds(BitmapSource image) {
        var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);int w=image.PixelWidth,h=image.PixelHeight;var pixels=new byte[w*h*4];converted.CopyPixels(pixels,w*4,0);
        int minX=w,minY=h,maxX=-1,maxY=-1;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[(y*w+x)*4+3]>24){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
        return maxX<minX?new Int32Rect():new Int32Rect(minX,minY,maxX-minX+1,maxY-minY+1);
    }
    public void Export(string folder) {
        if(!HighResolution)throw new Exception("高清素材尚未加载。");Directory.CreateDirectory(folder);
        string[] names={"待机","半闭眼","闭眼","工作中","向右移动","向左移动","招手","跳跃","等待输入","任务失败","完成","侧看"};
        for(int i=0;i<Keys.Length;i++){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(Keys[i]));using(var stream=File.Create(Path.Combine(folder,(i+1).ToString("00")+"-"+names[i]+".png")))encoder.Save(stream);}
        new HeadLook(Keys[0]).Export(Path.Combine(folder,"视线跟随"));
    }
}
}
