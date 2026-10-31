using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YoshinoPet {
// Each direction is one intact full-body drawing. Never splice head and body.
public sealed class HeadLook {
    public readonly BitmapSource[] Frames=new BitmapSource[9];
    public bool UsesDirectionalAtlas {get;private set;}
    public int SourceCellWidth {get;private set;}public int SourceCellHeight {get;private set;}
    public static int Direction(double dx,double dy,double deadZone) {
        if(dx*dx+dy*dy<deadZone*deadZone)return 4;
        int sector=((int)Math.Round(Math.Atan2(dy,dx)*4/Math.PI)+8)%8;
        return new[]{5,8,7,6,3,0,1,2}[sector];
    }
    public HeadLook(BitmapSource neutral) {
        string folder=Path.Combine(Paths.Base,"assets","hq");string cardinal=Path.Combine(folder,"look-cardinal.png"),diagonal=Path.Combine(folder,"look-diagonal.png");
        if(!File.Exists(cardinal)||!File.Exists(diagonal))throw new Exception("转头素材不完整，请完整解压软件。");
        Frames[4]=neutral;Import(cardinal,new[]{3,1,5,7},neutral);Import(diagonal,new[]{0,2,6,8},neutral);UsesDirectionalAtlas=true;
    }
    void Import(string path,int[] directions,BitmapSource neutral) {
        var atlas=new BitmapImage();atlas.BeginInit();atlas.CacheOption=BitmapCacheOption.OnLoad;atlas.UriSource=new Uri(path);atlas.EndInit();atlas.Freeze();
        int w=atlas.PixelWidth/2,h=atlas.PixelHeight/2;
        SourceCellWidth=SourceCellWidth==0?w:Math.Min(SourceCellWidth,w);SourceCellHeight=SourceCellHeight==0?h:Math.Min(SourceCellHeight,h);
        if(w<480||h<480)throw new Exception("转头源素材单格小于 480 像素。");
        var cells=new BitmapSource[4];var bounds=new Int32Rect[4];
        for(int i=0;i<4;i++) {var cell=new CroppedBitmap(atlas,new Int32Rect(i%2*w,i/2*h,w,h));cell.Freeze();cells[i]=SpriteArt.CleanAlpha(cell);bounds[i]=SpriteArt.AlphaBounds(cells[i]);if(bounds[i].Height<200)throw new Exception("转头素材不完整。");}
        // Fit each complete drawing to a consistent size and foot baseline.
        var target=SpriteArt.AlphaBounds(neutral);
        for(int i=0;i<4;i++) {
            var b=bounds[i];double scale=Math.Min(target.Height/(double)b.Height,(SpriteArt.FrameWidth-32.0)/b.Width),dw=b.Width*scale,dh=b.Height*scale;
            var crop=new CroppedBitmap(cells[i],b);crop.Freeze();
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(crop,new Rect((SpriteArt.FrameWidth-dw)/2,target.Y+target.Height-dh,dw,dh));
            var image=new RenderTargetBitmap(SpriteArt.FrameWidth,SpriteArt.FrameHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();Frames[directions[i]]=image;
        }
    }
    public BitmapSource Frame(int direction) {return Frames[direction];}
    public void Export(string folder) {
        Directory.CreateDirectory(folder);string[] names={"左上","上","右上","左","正面","右","左下","下","右下"};
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(31,35,46)),null,new Rect(0,0,SpriteArt.FrameWidth*3,SpriteArt.FrameHeight*3));
            for(int i=0;i<9;i++) {var frame=Frame(i);Save(frame,Path.Combine(folder,(i+1).ToString("00")+"-"+names[i]+".png"));dc.DrawImage(frame,new Rect(i%3*SpriteArt.FrameWidth,i/3*SpriteArt.FrameHeight,SpriteArt.FrameWidth,SpriteArt.FrameHeight));}
        }
        var sheet=new RenderTargetBitmap(SpriteArt.FrameWidth*3,SpriteArt.FrameHeight*3,96,96,PixelFormats.Pbgra32);sheet.Render(visual);Save(sheet,Path.Combine(folder,"九方向预览.png"));
    }
    static void Save(BitmapSource image,string path) {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);}
}
}
