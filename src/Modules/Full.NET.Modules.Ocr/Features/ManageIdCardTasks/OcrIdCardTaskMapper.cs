using Full.NET.Modules.Ocr.Contracts;
using Full.NET.Modules.Ocr.Domain;
using Full.NET.Modules.Ocr.Persistence;

namespace Full.NET.Modules.Ocr.Features.ManageIdCardTasks;

internal static class OcrIdCardTaskMapper
{
    public static OcrIdCardTaskResponse Map(OcrIdCardTaskRecord record, bool maskRecognizedFields) =>
        new(
            record.Id,
            record.SourceFileId,
            record.StatusKey,
            record.RecognizedName,
            maskRecognizedFields
                ? OcrIdCardMasking.MaskIdNumber(record.RecognizedIdNumber)
                : record.RecognizedIdNumber,
            record.RecognizedGender,
            record.RecognizedNation,
            record.RecognizedAddress,
            record.RecognizedBirthDate,
            record.ConfirmedName,
            record.ConfirmedIdNumber,
            record.ConfirmedGender,
            record.ConfirmedNation,
            record.ConfirmedAddress,
            record.ConfirmedBirthDate,
            record.FailureMessage,
            record.RecognizedAtUtc,
            record.ConfirmedAtUtc,
            record.RejectedAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.CreatedByUserId,
            record.Version);

    public static bool ShouldMask(OcrIdCardTaskRecord record) =>
        record.StatusKey == OcrIdCardTaskStatusKeys.Recognized;
}
