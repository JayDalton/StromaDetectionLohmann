using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading.Tasks;
using Glaukopis.SlideProcessing;
using Glaukopis.Core.Analysis;
using Glaukopis.SharpAccessoryIntegration.Filter;
//using Glaukopis.SharpAccessoryIntegration.Segmentation;
using SharpAccessory.Imaging.DigitalPathology;
using SharpAccessory.Imaging.Processors;
using SharpAccessory.Imaging.Segmentation;
using SharpAccessory.VirtualMicroscopy;
using SharpAccessory.Imaging.Filters;

namespace StromaDetectionExecute
{
  class Program
  {
    private static List<Eingabe> importItems = new List<Eingabe>();
    private static List<Ausgabe> exportItems = new List<Ausgabe>();

    static void Main(string[] args)
    {
      if (checkParams(args))
      {
        readInputFile(args[0]);

        processInput();

        writeResultFile(args[1]);

        Console.WriteLine();
        Console.WriteLine("Execution ended!");
        Console.ReadLine();
      }
      else
      {
        Console.WriteLine("Das Programm wurde nicht korrekt aufgerufen, oder die Eingabe CSV-Datei nicht gefunden.");
        Console.WriteLine("Der korrekte Aufruf ist: ");
        Console.WriteLine("StromaDetection.exe <eingabe.csv> <ausgabe.csv>");
        Console.ReadLine();
      }
    }

    private static void processInput()
    {
      int Radius = 2;
      int NoiseLevel = 10;

      Console.WriteLine("Processing Input...");
      foreach (var import in importItems)
      {
        Console.WriteLine();
        Console.WriteLine(import.FileName);

        Console.WriteLine("Slide extrahieren...");
        var processingHelper = new Processing(import.FileName);
        var slide = processingHelper.Slide;

        Console.WriteLine("Ausschnitt aus Slide extrahieren mit originaler Auflösung...");
        int partImageWidth = import.LowerRight.X - import.UpperLeft.X;
        int partImageHeight = import.LowerRight.Y - import.UpperLeft.Y;
        Bitmap partImage = slide.GetImagePart(
          import.UpperLeft.X, import.UpperLeft.Y,
          partImageWidth, partImageHeight,
          partImageWidth, partImageHeight
        );

        #region global tissue detection
        Console.WriteLine("Gewebe suchen und in separatem Layer speichern...");
        var bitmapProcessor = new BitmapProcessor(partImage);
        ObjectLayer overviewLayer = new TissueDetector().Execute(bitmapProcessor, Radius, NoiseLevel);
        bitmapProcessor.Dispose();

        Console.WriteLine("Gewebe-Layer in Ausschnitt zeichnen + speichern...");
        DrawObjectsToImage(partImage, overviewLayer, Color.Black);
        partImage.Save(processingHelper.DataPath + "ImagePartTissue.png");
        #endregion global tissue detection

        #region Deconvolution
        Console.WriteLine("Execute deconvolution 3...");
        var gpX = new ColorDeconvolution().Get3rdStain(partImage, ColorDeconvolution.KnownStain.HaematoxylinEosin);
        gpX.Dispose();
        Bitmap gpX_bmp = gpX.Bitmap;
        gpX_bmp.Save(processingHelper.DataPath + "ImagePartColor3.png");

        Console.WriteLine("Execute deconvolution 2...");
        var gpE = new ColorDeconvolution().Get2ndStain(partImage, ColorDeconvolution.KnownStain.HaematoxylinEosin);
        gpE.Dispose();
        Bitmap gpE_bmp = gpE.Bitmap;
        gpE_bmp.Save(processingHelper.DataPath + "ImagePartColor2.png");

        Console.WriteLine("Execute deconvolution 1...");
        var gpH = new ColorDeconvolution().Get1stStain(partImage, ColorDeconvolution.KnownStain.HaematoxylinEosin);
        gpH.Dispose();
        Bitmap gpH_bmp = gpH.Bitmap;
        gpH_bmp.Save(processingHelper.DataPath + "ImagePartColor1.png");
        #endregion Deconvolution

        #region execute edge detection
        Console.WriteLine("Execute edge detection...");
        SobelResponse responseH = Filtering.ExecuteSobel(gpH_bmp);
        SobelResponse responseE = Filtering.ExecuteSobel(gpE_bmp);
        var substracted = new double[responseH.Size.Width, responseH.Size.Height];
        var substractedRange = new Range<double>();
        for (var x = 0; x < responseH.Size.Width; x++)
        {
          for (var y = 0; y < responseH.Size.Height; y++)
          {
            var value = Math.Max(0, responseE.Gradient[x, y] - responseH.Gradient[x, y]);
            substracted[x, y] = value;
            substractedRange.Add(value);
          }
        }
        double[,] nonMaximumSupression = Filtering.ExecuteNonMaximumSupression(substracted, responseE.Orientation);
        Bitmap edges = Visualization.Visualize(nonMaximumSupression, Visualization.CreateColorizing(substractedRange.Maximum));
        edges.Save(processingHelper.DataPath + "ImagePartEdges.png");
        #endregion execute edge detection

        exportItems.Add(
          new Ausgabe {
            Identify = import.Identify, 
            Result = false, 
            Message = "kein Fehler" 
          }
        );
      }

    }

    private static void DrawObjectsToImage(Bitmap bitmap, ObjectLayer layer, Color color)
    {
      //*** Exception, falls eines der Parameter null ist **********************************************
      if (bitmap == null || layer == null || color == null) { throw new NullReferenceException("Parameter ungültig!"); }

      //*** Exception, falls Bild und Objektebene unterschiedliche Dimension haben *********************
      if (bitmap.Width != layer.Map.Width || bitmap.Height != layer.Map.Height) { throw new ArgumentOutOfRangeException("bitmap"); }

      //*** Bildprozessor anlegen **********************************************************************
      using (var processor = new BitmapProcessor(bitmap))
      {
        //*** Über alle Konturpunkte der Objekte iterieren *******************************************
        foreach (Point point in layer.Objects.ToArray().SelectMany(imageObject => imageObject.Contour.GetPoints()))
        {
          //*** Konturpunkt im Bild auf gegebene Farbe setzen **************************************
          processor.SetRed(point.X, point.Y, color.R);
          processor.SetGreen(point.X, point.Y, color.G);
          processor.SetBlue(point.X, point.Y, color.B);
        }
      }
    }

    private static void readInputFile(string fileName)
    {
      string line;
      StreamReader file = new StreamReader(fileName);
      while ((line = file.ReadLine()) != null)
      {
        importItems.Add(parseLine(line));
      }
      file.Close();
    }

    private static void writeResultFile(string resultFileName)
    {
      var resultFile = new StringBuilder();

      foreach (var item in exportItems)
      {
        resultFile.AppendLine(string.Format("{0},{1},{2}", item.Identify, item.Result, item.Message));
      }

      File.WriteAllText(resultFileName, resultFile.ToString());
    }

    private static Eingabe parseLine(string line)
    {
      string[] part = line.Split(new char[] { ';' });
      return new Eingabe
      {
        Identify = uint.Parse(part[0]),
        FileName = part[1],
        UpperLeft = new Point(int.Parse(part[2]), int.Parse(part[3])),
        LowerRight = new Point(int.Parse(part[4]), int.Parse(part[5]))
      };
    }

    private static bool checkParams(string[] parms)
    {
      bool result = false;
      if (parms.Length == 2 && File.Exists(parms[0]))
      {
        result = true;
      }
      return result;
    }

  }
}
