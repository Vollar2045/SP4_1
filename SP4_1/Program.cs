using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SP4_1
{
    static class Program
    {
        private static readonly Queue<string> Buffer = new();
        private static readonly object BufferLock = new();
        private const int MaxBufferSize = 1000;
        private static bool _readingCompleted;
        private static Thread _readerThread;
        private static Thread _reverserThread;
        private static int _readCount;
        private static int _writtenCount;
        private static int _totalWords;
        private static string _inputPath;
        private static string _outputPath;
        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _inputPath = args.Length > 0 ? args[0] : "input.txt";
            _outputPath = args.Length > 1 ? args[1] : "output.txt";
            if (!File.Exists(_inputPath))
            {
                Console.WriteLine($"Файл не найден: {_inputPath}");
                return;
            }
            _totalWords = EstimateWordCount(_inputPath);
            Console.WriteLine($"Входной файл:  {_inputPath}");
            Console.WriteLine($"Выходной файл: {_outputPath}");
            Console.WriteLine($"Слов: {_totalWords}");
            Console.WriteLine(new string('-', 60));
            var sw = Stopwatch.StartNew();
            _readerThread = new Thread(ReadWords) { Name = "Reader", IsBackground = true };
            _reverserThread = new Thread(ReverseAndSave) { Name = "Reverser", IsBackground = true };
            _readerThread.Start();
            _reverserThread.Start();
            while (_readerThread.IsAlive || _reverserThread.IsAlive)
            {
                ShowProgress();
                Thread.Sleep(100);
            }
            _readerThread.Join();
            _reverserThread.Join();
            sw.Stop();
            ShowProgress();
            Console.WriteLine();
            Console.WriteLine($"Готово за {sw.ElapsedMilliseconds} мс. " +
                              $"Прочитано: {_readCount}, записано: {_writtenCount}");
        }

        private static void ReadWords()
        {
            try
            {
                using var reader = new StreamReader(_inputPath, Encoding.UTF8);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    foreach (var word in SplitWords(line))
                    {
                        lock (BufferLock)
                        {
                            while (Buffer.Count >= MaxBufferSize)
                            {
                                Monitor.Wait(BufferLock);
                            }
                            Buffer.Enqueue(word);
                            Interlocked.Increment(ref _readCount);
                            Monitor.Pulse(BufferLock);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reader Ошибка: {ex.Message}");
            }
            finally
            {
                lock (BufferLock)
                {
                    _readingCompleted = true;
                    Monitor.PulseAll(BufferLock);
                }
            }
        }

        private static void ReverseAndSave()
        {
            try
            {
                using var writer = new StreamWriter(_outputPath, false, Encoding.UTF8);
                while (true)
                {
                    string word;
                    lock (BufferLock)
                    {
                        while (Buffer.Count == 0 && !_readingCompleted)
                        {
                            Monitor.Wait(BufferLock);
                        }
                        if (Buffer.Count == 0 && _readingCompleted)
                            break;
                        word = Buffer.Dequeue();
                        Monitor.Pulse(BufferLock);
                    }
                    var reversed = Reverse(word);
                    writer.WriteLine(reversed);
                    Interlocked.Increment(ref _writtenCount);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Reverser] Ошибка: {ex.Message}");
            }
        }
        
        private static string Reverse(string s)
        {
            var chars = s.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }

        private static IEnumerable<string> SplitWords(string line)
        {
            foreach (var w in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                yield return w;
        }

        private static int EstimateWordCount(string path)
        {
            try
            {
                int count = 0;
                using var reader = new StreamReader(path, Encoding.UTF8);
                string line;
                while ((line = reader.ReadLine()) != null)
                    count += line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                return count;
            }
            catch { return 0; }
        }

        private static void ShowProgress()
        {
            int read = Volatile.Read(ref _readCount);
            int written = Volatile.Read(ref _writtenCount);
            double percent = _totalWords > 0
                ? Math.Min(100.0, written * 100.0 / _totalWords)
                : 0;
            int barWidth = 30;
            int filled = (int)(percent / 100.0 * barWidth);
            string bar = new string('█', filled) + new string('░', barWidth - filled);
            Console.Write($"\r[{bar}] {percent,6:F1}%  " +
                          $"прочитано: {read,7}  записано: {written,7}");
        }
    }
}