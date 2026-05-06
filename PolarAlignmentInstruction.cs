using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility.Notification;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NirZonshine.NINA.TwoPointPolarAlignment {
    [ExportMetadata("Name", "2-Point Polar Alignment")]
    [ExportMetadata("Description", "Fast polar alignment using home position and a 90° RA rotation")]
    [ExportMetadata("Icon", "Telescope")]
    [ExportMetadata("Category", "Alignment")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class PolarAlignmentInstruction : SequenceItem {

        [ImportingConstructor]
        public PolarAlignmentInstruction() {
        }

        public PolarAlignmentInstruction(PolarAlignmentInstruction copyMe) : this() {
            CopyMetaData(copyMe);
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            Notification.ShowSuccess("2-Point Polar Alignment Instruction Executed (Stub)");
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new PolarAlignmentInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(PolarAlignmentInstruction)}";
        }
    }
}
