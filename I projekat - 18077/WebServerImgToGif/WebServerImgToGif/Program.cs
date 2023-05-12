using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Text;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using Image = SixLabors.ImageSharp.Image;
using System.Security.Cryptography;

namespace WebServerImgToGif;

public struct HttpContextTimer
{
    public HttpListenerContext context;
    public Stopwatch timer;
}

class Program
{
    private static object locker = new object();
     
    private static ReaderWriterLockSlim cacheLock = new ReaderWriterLockSlim();
    
    private static List<string> imageCache = new List<string>();
    
    private static Dictionary<string, byte[]> gifCache = new Dictionary<string, byte[]>();
    
    private static string gifCachePath = Path.Combine("C:\\Users\\Korisnik\\Desktop\\I PROJEKAT\\WebServerImgToGif\\WebServerImgToGif", "gifCache");
    
    private static string imagesPath = "../../../images";
    private static void LoadCacheFolder()
    {
        if (!Directory.Exists(gifCachePath))
        {
            Directory.CreateDirectory(gifCachePath);
            Console.WriteLine("Cache Folder created successfully.");
        }
        else
        {
            foreach(string imgPath in Directory.GetFiles(gifCachePath))
            {
                string filename = Path.GetFileName(imgPath);
                byte[] image_data = File.ReadAllBytes(imgPath);
                gifCache[filename] = image_data;
                string pngFilename = filename.Replace(".gif", ".png");
                imageCache.Add(pngFilename);
            }
            Console.WriteLine("Cache loaded successfully");
        }
    }
    private static byte[] ReadCache(string filename)
    {
        Console.WriteLine("Cita se kesirana slika...");
        cacheLock.EnterReadLock();
        try
        {
            return gifCache[filename];
        }
        finally
        {
            cacheLock.ExitReadLock();
        }
    }
    private static void WriteCache(string original, string filename, byte[] image_data)
    {
        Console.WriteLine("Nit upisuje sliku u kesu...");
        cacheLock.EnterWriteLock();
        try
        {
            imageCache.Add(original);
            gifCache.Add(filename, image_data);
        }
        finally
        {
            cacheLock.ExitWriteLock();
        }
    }

    static void Main(string[] args)
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5050/");
        listener.Start();
        //Image cache folder contains both requested original images and converted .gif images
        LoadCacheFolder();

        Console.WriteLine("Web server started at port 5050");
        Console.WriteLine(imageCache[0]);
        while (listener.IsListening)
        {
            HttpListenerContext context = listener.GetContext();
            HttpContextTimer httpContextTime = new HttpContextTimer();
            httpContextTime.context = context;
            httpContextTime.timer = new Stopwatch();
            httpContextTime.timer.Start();
            ProcessRequest(httpContextTime);
        }

        listener.Stop();
    }

    private static void ProcessRequest(HttpContextTimer httpContextTime)
    {
        if (!ThreadPool.QueueUserWorkItem(ProcessRequestExecute, httpContextTime))
        {
            httpContextTime.context.Response.StatusCode = 500;
            HttpResponse("500 - Connection Failed", null, httpContextTime);
        }
    }

    private static void ProcessRequestExecute(object state)
    {
        HttpContextTimer httpContextTime = (HttpContextTimer)state;

        HttpListenerContext context = httpContextTime.context;

        HttpListenerRequest request = context.Request;

        HttpListenerResponse response = context.Response;

        Console.WriteLine
            (
                $"Request received :\n" +
                $"User host name: {request.UserHostName}\n" +
                $"HTTP method: {request.HttpMethod}\n" +
                $"HTTP headers: {request.Headers}" +
                $"Content type: {request.ContentType}\n" +
                $"Content length: {request.ContentLength64}\n" +
                $"Cookies: {request.Cookies}\n"
            );

        byte[] res_data;
        response.StatusCode = 200;
        string query = request.Url.AbsolutePath;
        Console.WriteLine("query : " + query);
        string path = imagesPath + query;
        string filename = query.Substring(1);


        if (query == "imgCache")
        {
            response.StatusCode = 403;
            HttpResponse("403 - Access Denied", null, httpContextTime);
            return;
        }

        if (query == "/")
        {
            response.StatusCode = 404;
            HttpResponse("404 - Not Found", null, httpContextTime);;
            return;
        }

        if (imageCache.Contains(filename))
        {
            Console.WriteLine("Slika postoji u cache-u...");
            res_data = ReadCache(filename.Replace(".png", ".gif"));
            HttpResponse(filename, res_data, httpContextTime);
            return;
        }
        else if(!imageCache.Contains(filename))
        {
            HttpResponse("200", ImageToGif(path, filename), httpContextTime);
        }
    }

    private static void HttpResponse(string responseString, byte[]? res_data, HttpContextTimer httpContextTime)
    {
        HttpListenerResponse res = httpContextTime.context.Response;
        byte[] buffer;
        if (res_data != null)
        {
            buffer = res_data;
            res.ContentLength64 = res_data.Length;
        }
        else
        {
            buffer = Encoding.UTF8.GetBytes(responseString);
            res.ContentLength64 = 64;
        }
        res.ContentType = "image/gif";
        res.OutputStream.Write(buffer, 0, buffer.Length);
        httpContextTime.timer.Stop();
        Console.WriteLine
            (
                $"Response : \n" +
                $"Status code: {res.StatusCode}\n" +
                $"Content type: {res.ContentType}\n" +
                $"Content length: {res.ContentLength64}\n" +
                $"Time taken for response: {httpContextTime.timer.ElapsedMilliseconds} ms\n"+
                $"Body: {responseString}\n"
            );
    }
    private static byte[] ImageToGif(string path, string filename)
    {

        string gifFilename = filename.Replace(".png", ".gif");

        try
        {
            //ucitavanje slike i inicijalizovanje gif-a
            var pngImage = Image.Load<Rgba32>(path);
            var gifImage = new Image<Rgba32>(pngImage.Width, pngImage.Height);
            int numOfFrames = 10;
            for(int  i = 0; i < numOfFrames; i++)
            {
                var clone = pngImage.Clone();
                if (i % 2 == 0)
                {
                    clone.Mutate(x => x.Grayscale());
                }
                if (i % 3 == 0)
                {
                    clone.Mutate(x => x.ColorBlindness(ColorBlindnessMode.Deuteranopia));
                }
                if (i % 5 == 0)
                {
                    clone.Mutate(x => x.ColorBlindness(ColorBlindnessMode.Tritanopia));
                }
                gifImage.Frames.AddFrame(clone.Frames[0]);
 
                gifImage.Frames[gifImage.Frames.Count - 1].Metadata.GetGifMetadata().FrameDelay = 100;
            }

            string gifPath = gifCachePath + "/" + gifFilename;


            //upisivanje u cache folder

            Console.WriteLine("Ceka se upis u cache folderu...");
            lock (locker)
            {
                using FileStream stream = new FileStream(gifPath, FileMode.Create);
                gifImage.SaveAsGif(stream, new GifEncoder { ColorTableMode = GifColorTableMode.Local });
                Console.WriteLine("Gif uspesno upisan u cache folder.");
            }

            byte[] gif_data = File.ReadAllBytes(gifPath);
            WriteCache(filename, gifFilename, gif_data);
            return gif_data;

        }
        catch (Exception e)
        {
            Console.WriteLine($"Doslo je do greske prilikom konvertovanja slike sa izuzetkom : {e}");
            return null;
        }
    }
}