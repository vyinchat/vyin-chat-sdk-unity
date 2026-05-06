using System.Collections.Generic;
using System.Linq;
using Gamania.GIMChat.Internal.Data.DTOs;
using Gamania.GIMChat.Internal.Domain.Models;

namespace Gamania.GIMChat.Internal.Data.Mappers
{
    /// <summary>
    /// Mapper for converting between MessageDTO and MessageBO
    /// Data Layer responsibility: DTO ↔ Business Object conversion only
    /// JSON parsing is done by the caller (Repository, GIMChatMain)
    /// </summary>
    internal static class MessageDtoMapper
    {
        /// <summary>
        /// Convert MessageDTO to MessageBO (Business Object)
        /// </summary>
        internal static MessageBO ToBusinessObject(MessageDTO dto)
        {
            if (dto == null)
                return null;

            // File fields
            var fileObj = dto.file;
            var fileUrl = fileObj?.url ?? "";
            var fileName = fileObj?.name ?? "";
            var fileMimeType = fileObj?.type ?? "";
            var fileSize = fileObj?.size ?? 0;
            var objectId = fileObj?.object_id ?? "";
            var objectStatus = fileObj?.object_status ?? "";
            var requireAuth = fileObj?.require_auth ?? false;

            return new MessageBO
            {
                MessageId = dto.message_id != 0 ? dto.message_id : dto.msg_id,
                Message = dto.message,
                ChannelUrl = dto.channel_url,
                ChannelType = dto.channel_type,
                CreatedAt = dto.created_at != 0 ? dto.created_at : dto.ts,
                Done = dto.done,
                CustomType = dto.custom_type ?? "",
                Data = dto.data ?? "",
                ReqId = dto.request_id ?? dto.req_id ?? "",
                Sender = ToSenderBO(dto.user),
                FileUrl = fileUrl,
                FileName = fileName,
                FileMimeType = fileMimeType,
                FileSize = fileSize,
                RequireAuth = requireAuth,
                ObjectId = objectId,
                ObjectStatus = objectStatus,
                Thumbnails = dto.thumbnails?.Select(t => new ThumbnailBO
                {
                    MaxWidth = t.width,
                    MaxHeight = t.height,
                    RealWidth = t.real_width,
                    RealHeight = t.real_height,
                    Url = t.url ?? ""
                }).ToList()
            };
        }

        /// <summary>
        /// Convert SenderDTO to SenderBO
        /// </summary>
        internal static SenderBO ToSenderBO(SenderDTO dto)
        {
            if (dto == null)
                return null;

            return new SenderBO
            {
                UserId = dto.user_id ?? dto.guest_id ?? "",
                Nickname = dto.name ?? dto.nickname ?? "",
                ProfileUrl = dto.image ?? dto.profile_url ?? "",
                Role = ParseRole(dto.role)
            };
        }

        internal static RoleBO ParseRole(string role)
        {
            if (string.IsNullOrEmpty(role))
                return RoleBO.None;

            return role.ToLowerInvariant() switch
            {
                "operator" => RoleBO.Operator,
                _ => RoleBO.None
            };
        }
    }
}
