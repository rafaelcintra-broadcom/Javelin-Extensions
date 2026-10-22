using System;
using System.Activities;
using System.ComponentModel;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe Dataset Allocate")]
    [Description("Allocates (creates) or deletes a mainframe Sequential (PS), Partitioned (PDS/PDSE), or VSAM Data Set on z/OS")]
    public class ZoweDatasetAllocateActivity : ZoweBaseActivity
    {
        [Category("Operation")]
        [RequiredArgument]
        [DisplayName("Action")]
        [Description("Action: CreatePS (Sequential), CreatePDS (Partitioned), CreatePDSE, CreateLike (Model DS), or Delete")]
        [DefaultValue("CreatePS")]
        public InArgument<string> Action { get; set; }

        [Category("Operation")]
        [RequiredArgument]
        [DisplayName("Data Set Name")]
        [Description("Name of dataset to allocate or delete, e.g. 'HLQ.NEW.DATASET'")]
        public InArgument<string> DatasetName { get; set; }

        [Category("Dataset Attributes")]
        [DisplayName("Model Data Set (Like)")]
        [Description("Existing dataset to copy allocation attributes from (when Action=CreateLike)")]
        public InArgument<string> LikeDataset { get; set; }

        [Category("Dataset Attributes")]
        [DisplayName("Record Format (RECFM)")]
        [Description("FB, VB, F, V, U (default: FB)")]
        [DefaultValue("FB")]
        public InArgument<string> RecordFormat { get; set; }

        [Category("Dataset Attributes")]
        [DisplayName("Record Length (LRECL)")]
        [DefaultValue(80)]
        public InArgument<int?> RecordLength { get; set; }

        [Category("Dataset Attributes")]
        [DisplayName("Block Size (BLKSIZE)")]
        [DefaultValue(3120)]
        public InArgument<int?> BlockSize { get; set; }

        [Category("Dataset Attributes")]
        [DisplayName("Primary Space / Allocation Units")]
        [DefaultValue(5)]
        public InArgument<int?> PrimarySpace { get; set; }

        [Category("Dataset Attributes")]
        [DisplayName("Secondary Space")]
        [DefaultValue(5)]
        public InArgument<int?> SecondarySpace { get; set; }

        [Category("Dataset Attributes")]
        [DisplayName("Volume Serial (VOLSER)")]
        [Description("Optional volume serial")]
        public InArgument<string> VolumeSerial { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string ds = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                string action = Action.Get(context) ?? "CreatePS";
                ds = DatasetName.Get(context);
                string likeDs = LikeDataset.Get(context);
                string recfm = RecordFormat.Get(context);
                int? lrecl = RecordLength.Get(context);
                int? blksz = BlockSize.Get(context);
                int? pri = PrimarySpace.Get(context);
                int? sec = SecondarySpace.Get(context);
                string vol = VolumeSerial.Get(context);

                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(ds))
                    throw new ArgumentException("Data Set Name não pode ser vazio.");

                string cmdArgs;
                if (action.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                {
                    cmdArgs = $"zos-files delete data-set \"{ds.Trim()}\" --for-sure";
                }
                else if (action.Equals("CreateLike", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(likeDs))
                {
                    if (string.IsNullOrWhiteSpace(likeDs))
                        throw new ArgumentException("Model Data Set (Like) deve ser especificado para CreateLike.");

                    cmdArgs = $"zos-files create data-set-classic \"{ds.Trim()}\" --like \"{likeDs.Trim()}\"";
                }
                else if (action.Equals("CreatePDS", StringComparison.OrdinalIgnoreCase))
                {
                    cmdArgs = $"zos-files create data-set-partitioned \"{ds.Trim()}\"";
                    if (!string.IsNullOrWhiteSpace(recfm)) cmdArgs += $" --record-format {recfm}";
                    if (lrecl.HasValue) cmdArgs += $" --record-length {lrecl.Value}";
                    if (blksz.HasValue) cmdArgs += $" --block-size {blksz.Value}";
                    if (pri.HasValue) cmdArgs += $" --primary-space {pri.Value}";
                    if (sec.HasValue) cmdArgs += $" --secondary-space {sec.Value}";
                    if (!string.IsNullOrWhiteSpace(vol)) cmdArgs += $" --volume-serial \"{vol}\"";
                }
                else if (action.Equals("CreatePDSE", StringComparison.OrdinalIgnoreCase))
                {
                    cmdArgs = $"zos-files create data-set-classic \"{ds.Trim()}\" --dataset-type LIBRARY";
                    if (!string.IsNullOrWhiteSpace(recfm)) cmdArgs += $" --record-format {recfm}";
                    if (lrecl.HasValue) cmdArgs += $" --record-length {lrecl.Value}";
                    if (blksz.HasValue) cmdArgs += $" --block-size {blksz.Value}";
                    if (pri.HasValue) cmdArgs += $" --primary-space {pri.Value}";
                    if (sec.HasValue) cmdArgs += $" --secondary-space {sec.Value}";
                    if (!string.IsNullOrWhiteSpace(vol)) cmdArgs += $" --volume-serial \"{vol}\"";
                }
                else
                {
                    // CreatePS (Sequential)
                    cmdArgs = $"zos-files create data-set-sequential \"{ds.Trim()}\"";
                    if (!string.IsNullOrWhiteSpace(recfm)) cmdArgs += $" --record-format {recfm}";
                    if (lrecl.HasValue) cmdArgs += $" --record-length {lrecl.Value}";
                    if (blksz.HasValue) cmdArgs += $" --block-size {blksz.Value}";
                    if (pri.HasValue) cmdArgs += $" --primary-space {pri.Value}";
                    if (sec.HasValue) cmdArgs += $" --secondary-space {sec.Value}";
                    if (!string.IsNullOrWhiteSpace(vol)) cmdArgs += $" --volume-serial \"{vol}\"";
                }

                var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                IsSuccess.Set(context, execResult.Success);

                if (execResult.Success)
                {
                    ErrorMessage.Set(context, string.Empty);
                }
                else
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, $"Zowe Dataset {action}", ds);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe Dataset Allocate", ds);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
