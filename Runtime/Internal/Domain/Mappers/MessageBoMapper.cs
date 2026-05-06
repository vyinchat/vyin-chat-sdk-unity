using System.Collections.Generic;
using System.Linq;
using Gamania.GIMChat.Internal.Domain.Models;

namespace Gamania.GIMChat.Internal.Domain.Mappers
{
    public static class MessageBoMapper
    {
        public static GimBaseMessage ToPublicModel(MessageBO bo)
        {
            if (bo == null)
                return null;

            if (bo.IsFileMessage)
                return ToFileMessage(bo);

            return new GimBaseMessage
            {
                MessageId = bo.MessageId,
                Message = bo.Message,
                ChannelUrl = bo.ChannelUrl,
                ChannelType = ParseChannelType(bo.ChannelType),
                CreatedAt = bo.CreatedAt,
                Done = bo.Done,
                CustomType = bo.CustomType,
                Data = bo.Data,
                ReqId = bo.ReqId,
                Sender = ToPublicSender(bo.Sender),
                SendingStatus = bo.MessageId > 0 ? GimSendingStatus.Succeeded : GimSendingStatus.None
            };
        }

        private static GimFileMessage ToFileMessage(MessageBO bo)
        {
            return new GimFileMessage
            {
                MessageId = bo.MessageId,
                Message = bo.Message,
                ChannelUrl = bo.ChannelUrl,
                ChannelType = ParseChannelType(bo.ChannelType),
                CreatedAt = bo.CreatedAt,
                Done = bo.Done,
                CustomType = bo.CustomType,
                Data = bo.Data,
                ReqId = bo.ReqId,
                Sender = ToPublicSender(bo.Sender),
                PlainUrl = bo.FileUrl ?? "",
                Name = bo.FileName ?? "",
                Size = bo.FileSize,
                MimeType = bo.FileMimeType ?? "",
                RequireAuth = bo.RequireAuth,
                ObjectId = bo.ObjectId,
                ObjectStatus = bo.ObjectStatus ?? "",
                Thumbnails = bo.Thumbnails?.Select(t => new GimThumbnail
                {
                    MaxWidth = t.MaxWidth,
                    MaxHeight = t.MaxHeight,
                    RealWidth = t.RealWidth,
                    RealHeight = t.RealHeight,
                    PlainUrl = t.Url ?? "",
                    RequireAuth = bo.RequireAuth
                }).ToList() ?? new List<GimThumbnail>()
            };
        }

        public static GimSender ToPublicSender(SenderBO bo)
        {
            if (bo == null)
                return null;

            return new GimSender
            {
                UserId = bo.UserId,
                Nickname = bo.Nickname,
                ProfileUrl = bo.ProfileUrl,
                Role = ToPublicRole(bo.Role)
            };
        }

        internal static GimRole ToPublicRole(RoleBO role)
        {
            return role switch
            {
                RoleBO.Operator => GimRole.Operator,
                _ => GimRole.None
            };
        }

        public static MessageBO ToBusinessObject(GimBaseMessage model)
        {
            if (model == null)
                return null;

            var bo = new MessageBO
            {
                MessageId = model.MessageId,
                Message = model.Message,
                ChannelUrl = model.ChannelUrl,
                ChannelType = model.ChannelType == GimChannelType.Open ? "open" : "group",
                CreatedAt = model.CreatedAt,
                Done = model.Done,
                CustomType = model.CustomType,
                Data = model.Data,
                ReqId = model.ReqId,
                Sender = ToSenderBO(model.Sender)
            };

            if (model is GimFileMessage fileMsg)
            {
                bo.FileUrl = fileMsg.PlainUrl;
                bo.FileName = fileMsg.Name;
                bo.FileMimeType = fileMsg.MimeType;
                bo.FileSize = fileMsg.Size;
                bo.RequireAuth = fileMsg.RequireAuth;
                bo.ObjectId = fileMsg.ObjectId;
                bo.ObjectStatus = fileMsg.ObjectStatus;
                bo.Thumbnails = fileMsg.Thumbnails?.Select(t => new ThumbnailBO
                {
                    MaxWidth = t.MaxWidth,
                    MaxHeight = t.MaxHeight,
                    RealWidth = t.RealWidth,
                    RealHeight = t.RealHeight,
                    Url = t.PlainUrl ?? ""
                }).ToList();
            }

            return bo;
        }

        public static SenderBO ToSenderBO(GimSender model)
        {
            if (model == null)
                return null;

            return new SenderBO
            {
                UserId = model.UserId,
                Nickname = model.Nickname,
                ProfileUrl = model.ProfileUrl,
                Role = ToRoleBO(model.Role)
            };
        }

        internal static RoleBO ToRoleBO(GimRole role)
        {
            return role switch
            {
                GimRole.Operator => RoleBO.Operator,
                _ => RoleBO.None
            };
        }

        internal static GimChannelType ParseChannelType(string channelType)
        {
            return string.Equals(channelType, "open", System.StringComparison.OrdinalIgnoreCase)
                ? GimChannelType.Open
                : GimChannelType.Group;
        }
    }
}
