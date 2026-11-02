using System;
using System.Collections.Generic;
using System.IO;

namespace ModelSheetPublisher
{
    public sealed class OutputRecoveryException : IOException
    {
        public OutputRecoveryException(string folder, Exception inner)
            : base("Не удалось полностью восстановить прежние файлы. Резервные копии сохранены: " + folder, inner) { }
    }

    public static class OutputCommitter
    {
        // Stage and targets must be on the same volume. File.Replace keeps the old file as a backup.
        // Keep backups until the entire batch succeeds; roll back earlier targets if a later one fails.
        public static void Commit(IList<string> staged, IList<string> targets, bool overwrite, string stage)
        {
            if (staged.Count != targets.Count) throw new ArgumentException("Output counts differ.");
            var completed = new List<Tuple<string, string>>();
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    string backup = null;
                    if (File.Exists(targets[i]))
                    {
                        if (!overwrite) throw new IOException("Файл уже существует: " + targets[i]);
                        backup = Path.Combine(stage, "backup-" + i.ToString("D4") + ".pdf");
                        File.Replace(staged[i], targets[i], backup);
                    }
                    else File.Move(staged[i], targets[i]);
                    completed.Add(Tuple.Create(targets[i], backup));
                }
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                for (int i = completed.Count - 1; i >= 0; i--)
                {
                    var item = completed[i];
                    try
                    {
                        if (item.Item2 == null) File.Delete(item.Item1);
                        else if (File.Exists(item.Item1)) File.Replace(item.Item2, item.Item1, null);
                        else File.Move(item.Item2, item.Item1);
                    }
                    catch (Exception rollbackError) { errors.Add(rollbackError); }
                }
                if (errors.Count > 1) throw new OutputRecoveryException(stage, new AggregateException(errors));
                throw;
            }
        }
    }
}
