# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-05-06

### Added

- **Channel List Filtering**
  - `GimGroupChannelListQuery` now supports `IncludeEmpty` and `CustomType` filters
  - New `GIMChat.CreateGroupChannelCollection(GimGroupChannelListQueryParams)` overload for full query control; the `int limit` variant is deprecated
- **Open Channel API** — *Newly supported.*
  - Create, enter, and send messages via `GimOpenChannel` and `GimOpenChannelModule`
  - Receive real-time events via `GimOpenChannelHandler`
  - List current participants via `GimOpenChannel.CreateParticipantListQuery()`
- **File Message API** — *Newly supported.*
  - Send and receive file messages via `GimGroupChannel.SendFileMessage()`
  - Supports multipart upload, progress callbacks, and large file handling
- **User Management APIs** — *Newly supported.*
  - Each channel member now exposes `MemberState`, `Role`, and `IsMuted` via `GimMember`
  - Ban and mute users within a channel via `GimGroupChannel`; block users globally via `GIMChat`
  - Query banned, muted, blocked, and application users via the corresponding list query APIs

### Changed

- **`GimGroupChannel.Members`** — Type changed from `List<GimUser>` to `List<GimMember>`. `GimMember` extends `GimUser` so existing property access (`UserId`, `Nickname`, etc.) is unaffected; only explicit `List<GimUser>` variable assignments will need to be updated

### Fixed

- **Last Message** — `GimGroupChannel.LastMessage` now correctly reflects the most recent message returned from the server
- **Message Sending Status** — Messages loaded from the server now correctly report `Succeeded` instead of `None`

## [1.1.3] - 2026-04-23

### Fixed

- **IL2CPP compatibility (UPM)** — Fixed connection failure on IL2CPP builds when installed via Unity Package Manager

## [1.1.2] - 2026-04-23

### Fixed

- **IL2CPP compatibility** — Fixed connection failure on devices built with high code stripping settings
- **Message history** — Fixed `GIMChat.CreateMessageCollection(channel)` loading from the oldest messages instead of the latest when called without an explicit starting point

## [1.1.1] - 2026-04-22

### Fixed

- **Connection error logging** — Improved error visibility when connection establishment fails on Android devices

## [1.1.0] - 2026-04-08

### Added

- **Current user profile update API** — Added `GIMChat.UpdateCurrentUserInfo()` and `UpdateCurrentUserInfoAsync()` with `GimUserUpdateParams`, with improved `GIMChat.CurrentUser` synchronization and more consistent profile updates in message-related views
- **Streaming error codes** — Added SSE / bot engine related error codes `645012` and `645013`

### Fixed

- **Message collection starting point** — Corrected `GimMessageCollection` starting point semantics

## [1.0.0] - 2026-03-12

Initial public release of Vyin Chat Unity SDK.

### Features

- **Initialization** — Configure the SDK with your App ID via `GIMChat.Init()`
- **User connection** — Connect and authenticate users with `GIMChat.Connect()`, with session token refresh support via `IGimSessionHandler`
- **Group channels** — Create and retrieve distinct group channels via `GimGroupChannelModule`
- **Send messages** — Send user messages with immediate pending state and delivery confirmation via `GimGroupChannel.SendUserMessage()`
- **Real-time messaging** — Receive incoming messages via `GimGroupChannelHandler` callbacks or the recommended `GimMessageCollection` which handles both message history and real-time updates
- **Connection management** — Automatic reconnection with exponential backoff, connection state tracking via `GimConnectionState`, and background disconnection support
- **Message reliability** — Sending status tracking (`Pending`, `Succeeded`, `Failed`) and optional auto-resend on reconnection
- **Async/await support** — All APIs available in both callback and async/await variants
