using System;
using System.Collections.Generic;

namespace CarpaNet.Jetstream;

/// <summary>
/// Converts and validates planSnapshot wire responses into the public plan model. The wire
/// fields come from the server, so every range is checked before use.
/// </summary>
internal static class JetstreamV2PlanConverter
{
    public static JetstreamSnapshotPlan Convert(JetstreamV2PlanSnapshotOutput output)
    {
        if (output.PlannedThroughSeq < 0 || output.SealedTipSeq < 0)
        {
            throw new JetstreamV2Exception("planSnapshot returned a negative seq");
        }

        if (output.PlannedThroughSeq > output.SealedTipSeq)
        {
            throw new JetstreamV2Exception(
                $"planSnapshot plannedThroughSeq {output.PlannedThroughSeq} exceeds sealedTipSeq {output.SealedTipSeq}");
        }

        var segments = new List<JetstreamPlannedSegment>(output.Segments?.Count ?? 0);
        if (output.Segments != null)
        {
            foreach (var dto in output.Segments)
            {
                segments.Add(ConvertSegment(dto));
            }
        }

        return new JetstreamSnapshotPlan
        {
            PlannedThroughSeq = output.PlannedThroughSeq,
            SealedTipSeq = output.SealedTipSeq,
            Segments = segments,
        };
    }

    private static JetstreamPlannedSegment ConvertSegment(JetstreamV2PlanSegmentDto dto)
    {
        if (string.IsNullOrEmpty(dto.Name))
        {
            throw new JetstreamV2Exception($"planSnapshot segment missing name (index {dto.Index})");
        }

        if (dto.Index < 0 || dto.Index > int.MaxValue || dto.MinSeq < 0 || dto.MaxSeq < dto.MinSeq)
        {
            throw new JetstreamV2Exception($"planSnapshot segment \"{dto.Name}\" has an invalid index or seq range");
        }

        var segment = new JetstreamPlannedSegment
        {
            Name = dto.Name!,
            Index = (int)dto.Index,
            Checksum = dto.Checksum ?? string.Empty,
            MinSeq = dto.MinSeq,
            MaxSeq = dto.MaxSeq,
        };

        switch (dto.Mode)
        {
            case "segment":
                segment.Mode = JetstreamSegmentPlanMode.WholeSegment;
                break;
            case "blocks":
                segment.Mode = JetstreamSegmentPlanMode.Blocks;
                if (dto.Blocks == null || dto.Blocks.Count == 0)
                {
                    throw new JetstreamV2Exception($"planSnapshot segment \"{dto.Name}\" has mode=blocks but no block ranges");
                }

                var ranges = new List<JetstreamBlockRange>(dto.Blocks.Count);
                foreach (var range in dto.Blocks)
                {
                    if (range.First < 0 || range.Last < range.First || range.Last > int.MaxValue)
                    {
                        throw new JetstreamV2Exception(
                            $"planSnapshot segment \"{dto.Name}\" has invalid block range [{range.First},{range.Last}]");
                    }

                    ranges.Add(new JetstreamBlockRange { First = (int)range.First, Last = (int)range.Last });
                }

                segment.Blocks = ranges;
                break;
            default:
                throw new JetstreamV2Exception($"planSnapshot segment \"{dto.Name}\" has unknown mode \"{dto.Mode}\"");
        }

        return segment;
    }
}
