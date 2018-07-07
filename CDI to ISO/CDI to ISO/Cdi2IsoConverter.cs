using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace CDI_to_ISO
{
    internal enum ConversionResult
    {
        Success,
        ConversionCanceled,
        IoException
    }

    internal static class Cdi2IsoConverter
    {
        private static readonly byte[] SyncHeader =
        {
            0x00,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0x00
        };

        //Unnamed constants
        private const int Iso9660Pos = 32768;
        private const int SyncHeaderMdfPos = 2352;

        public static int ProgressMax { get; set; } = 100;

        //log strings
        private static int conversionId = 1;

        private const string NormalImageLog = "Detected normal CDI image.";
        private const string RawImageLog = "Detected raw CDI image.";
        private const string PQImageLog = "Detected PQ CDI image.";
        private const string CDGImageLog = "Detected CD+G CDI image.";
        
        private const string StartingConversionLog = "File format ok, starting conversion...";
        private const string ConversionCompletedLog = "Conversion completed";
        private const string ConversionCanceledLog = "Conversion cancelled by user.";
        private const string IoExceptionLog = "Exception while accessing files.";
        private static string ConversionProgressLog(int currentStep) => $"Conversion {currentStep}% done";
        
        private static string StartingNewConversionLog() => $"Starting conversion #{conversionId++}";

        private const string StartingCopyLog = "Starting copy of contents...";
        private const string CopyCompletedLog = "Copy completed";
        private const string CopyCanceledLog = "Copy cancelled by user.";

        private static string CopyProgressLog(int currentStep) => $"Copy {currentStep}% done";

        public static async Task<ConversionResult> ConvertAsync(
            StorageFile mdfFile,
            StorageFile isoFile,
            IProgress<int> progress = null,
            StreamWriter log = null,
            CancellationToken token = default(CancellationToken)
        )
        {
            if(conversionId != 1)
                log?.WriteLine();
            log?.WriteLine(StartingNewConversionLog());

            try
            {
                using (Stream sourceStream = await mdfFile.OpenStreamForReadAsync())
                {
                    int seekEcc,
                        sectorSize,
                        sectorData = 2048,
                        seekHeader;

                    byte[] syncHeaderBuffer = new byte[SyncHeader.Length];

                    sourceStream.Read(syncHeaderBuffer, 0, SyncHeader.Length);
                    if (!syncHeaderBuffer.SequenceEqual(SyncHeader)) //59 negated
                    {
                        //97
                        log?.WriteLine(NormalImageLog);
                        seekHeader = 0;
                        sectorSize = 2048;
                        seekEcc = 0;
                    }
                    else
                    {
                        //61
                        seekHeader = 16;

                        sourceStream.Seek(2352, SeekOrigin.Begin);
                        sourceStream.Read(syncHeaderBuffer, 0, SyncHeader.Length);

                        if (syncHeaderBuffer.SequenceEqual(SyncHeader))
                        {
                            //69
                            log?.WriteLine(RawImageLog);
                            sectorSize = 2352;
                            seekEcc = 288;
                        }
                        else
                        {
                            //76
                            sourceStream.Seek(2368, SeekOrigin.Begin);
                            sourceStream.Read(syncHeaderBuffer, 0, SyncHeader.Length);

                            if (syncHeaderBuffer.SequenceEqual(SyncHeader))
                            {
                                //82
                                log?.WriteLine(PQImageLog);
                                seekEcc = 304;
                                sectorSize = 2368;
                            }
                            else
                            {
                                //89
                                log?.WriteLine(CDGImageLog);
                                seekEcc = 384;
                                sectorSize = 2448;
                            }
                        }
                    }

                    log?.WriteLine(StartingConversionLog);

                    //103
                    using (Stream destStream = await isoFile.OpenStreamForWriteAsync())
                    {
                        destStream.SetLength(0);
                        long sourceSectorsCount = sourceStream.Length / sectorSize;
                       
                        sourceStream.Seek(0, SeekOrigin.Begin);
                        var sectorBuf = new byte[sectorData];

                        int bw = 1;

                        int lastReportedProgress = 0;
                        for (int i = 0; i < sourceSectorsCount; i++) //107
                        {
                            sourceStream.Seek(seekHeader, SeekOrigin.Current);
                            await sourceStream.ReadAsync(sectorBuf, 0, sectorData, token);

                            if (bw > 150)
                            {
                                if(sourceSectorsCount != i)
                                    await destStream.WriteAsync(sectorBuf, 0, sectorData, token);
                                else
                                    await destStream.WriteAsync(sectorBuf, 0, 1559, token);
                            }
                            
                            //118
                            sourceStream.Seek(seekEcc, SeekOrigin.Current);
                            bw++;

                            int currentProgress = (int)(i * ProgressMax / sourceSectorsCount);
                            if (currentProgress > lastReportedProgress)
                            {
                                progress?.Report(currentProgress);
                                log?.WriteLine(ConversionProgressLog(currentProgress));
                                lastReportedProgress = currentProgress;
                            }
                        }
                        //122
                    }
                }

                progress?.Report(ProgressMax);
                log?.WriteLine(ConversionCompletedLog);
                return ConversionResult.Success;
            }
            catch(IOException)
            {
                log?.WriteLine(IoExceptionLog);
                return ConversionResult.IoException;
            }
            catch (OperationCanceledException)
            {
                log?.WriteLine(ConversionCanceledLog);
                return ConversionResult.ConversionCanceled;
            }
        }
    }
}
