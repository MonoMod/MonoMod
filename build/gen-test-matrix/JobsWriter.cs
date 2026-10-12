using GenTestMatrix.Models;
using System.Text;
using System.Text.Json;

namespace GenTestMatrix
{
    internal sealed class JobsWriter : IAsyncDisposable
    {
        private readonly Dictionary<string, Dictionary<string, List<Job>>> jobs = new();
        private readonly List<JobBatch> batches = new();
        private readonly FileStream outputStream;
        private readonly StreamWriter writer;
        private readonly string[] outputNames;
        private int nextOutput;

        public JobsWriter(FileStream outputStream, string[] outputNames)
        {
            this.outputStream = outputStream;
            writer = new(outputStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            this.outputNames = outputNames;
        }

        public void AddJob(Job job, string osName, string runtimeName)
        {
            if (!jobs.TryGetValue(osName, out var osJobs))
            {
                jobs.Add(osName, osJobs = new());
            }

            if (!osJobs.TryGetValue(runtimeName, out var rtJobs))
            {
                osJobs.Add(runtimeName, rtJobs = new());
            }

            rtJobs.Add(job);
        }

        public async ValueTask Write()
        {
            var batches = jobs
                .Select(kvp => new JobBatch
                {
                    Title = kvp.Key,
                    Matrix = new()
                    {
                        Jobs = kvp.Value.Select(kvp => new JobGroup
                        {
                            Title = kvp.Key,
                            Matrix = new() { Jobs = kvp.Value }
                        })
                    }
                });

            foreach (var batch in batches)
            {
                this.batches.Add(batch);
                if (this.batches.Count == Constants.MaxJobCountPerMatrix)
                {
                    await FlushAsync();
                }
            }

            jobs.Clear();
        }

        public async ValueTask FlushAsync()
        {
            if (nextOutput >= outputNames.Length)
            {
                throw new InvalidOperationException($"Not enough output names were specified (need at least {nextOutput})");
            }

            var outName = outputNames[nextOutput++];
            await writer.WriteAsync(outName);
            await writer.WriteAsync('=');
            await writer.FlushAsync();

            await JsonSerializer.SerializeAsync(outputStream, new MatrixResult<JobBatch> { Jobs = batches }, JsonCtx.Default.MatrixResultJobBatch);
            await writer.WriteLineAsync();
            batches.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await FlushAsync();
            await writer.DisposeAsync();
            await outputStream.DisposeAsync();
        }
    }
}
