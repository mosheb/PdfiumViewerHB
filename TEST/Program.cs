using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TEST
{
    class Program
    {
        static void Main(string[] args)
        {
            int pagenum = 1;

            // dpi = 96 here for screen display
            // for printing up it to 240, 300, 600 or whatever

            using (PdfiumViewerHB.PdfDocument pdoc = PdfiumViewerHB.PdfDocument.Load(@"c:\users\joe\mikado.pdf"))
            {
                //rasterize whole page------------------------------
                var img = pdoc.Render(pagenum, 96, 96, false);

                //get single page as new pdf doc (byte[])-----------
                var pdfpage = pdoc.GetPage(pagenum);

                //rasterize page area-------------------------------
                //todo: move implemenation into pdfdocument class
                var mb = pdoc.GetPageMediaBox(pagenum);
                /*get your vals:
                 * starting from pdf page width  & height
                 * get the coords of the crop box
                 * and crop box width & height
                 * */
                mb.Left = 200;
                mb.Right = 200;
                mb.Top = 200;
                mb.Bottom = 200;
                pdoc.SetPageMediaBox(pagenum, mb.Left, mb.Right, mb.Top, mb.Bottom);
                //if pdfpage with is 1000 then cropbox width will now be 600
                //if pdfpage with is 1500 then cropbox width will now be 1100
                Bitmap bmp = new Bitmap(600, 1100);
                var g = Graphics.FromImage(bmp);
                pdoc.Render(pagenum, g, 96, 96, new Rectangle(0, 0, 600, 1100), false);
                using (var stream = new MemoryStream())
                {
                    bmp.Save(stream, ImageFormat.Png);
                    //save the stream somewhere, convert to byte[] etc
                }
            }
        }
    }
}

