using System;
using System.Collections.Generic;
using Chameleon.Core.Catalog;
using Chameleon.Core.Generation;

// Wire models of contracts/cloud-api.openapi.yaml. One file because they only exist to be (de)serialized.
#pragma warning disable CS1591 // Members mirror the contract, which documents them.
namespace Chameleon.Core.Cloud
{
    /// <summary>Answer to "start device authorization".</summary>
    public sealed class DeviceAuthorization
    {
        public string DeviceCode { get; set; } = string.Empty;
        public string UserCode { get; set; } = string.Empty;
        public string VerificationUrl { get; set; } = string.Empty;
        public string VerificationUrlComplete { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public int Interval { get; set; } = 3;
    }

    /// <summary>Tokens issued after login or refresh.</summary>
    public sealed class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public string RefreshToken { get; set; } = string.Empty;
        public CloudUser? User { get; set; }
    }

    /// <summary>The signed-in user.</summary>
    public sealed class CloudUser
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Credits { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>Answer to redeeming a code.</summary>
    public sealed class RedeemResult
    {
        public int CreditsAdded { get; set; }
        public int Credits { get; set; }
    }

    /// <summary>One credit movement.</summary>
    public sealed class LedgerEntry
    {
        public long Id { get; set; }
        public int Delta { get; set; }
        public int BalanceAfter { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? RefType { get; set; }
        public string? RefId { get; set; }
        public string? Note { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>A page of a cursor-paginated list.</summary>
    public sealed class Page<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public string? NextCursor { get; set; }
    }

    /// <summary>A model offered by the gateway, with its price per size tier.</summary>
    public sealed class CloudCatalogModel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public BackendCapabilities Capabilities { get; set; } = new BackendCapabilities();
        public Dictionary<string, int> Prices { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>Gateway limits that apply to every user.</summary>
    public sealed class CloudLimits
    {
        public int MaxConcurrency { get; set; } = 1;
        public long MaxUploadBytes { get; set; }
    }

    /// <summary>The gateway's catalog: same shape as the built-in one plus prices and limits.</summary>
    public sealed class CloudCatalog
    {
        public string Version { get; set; } = string.Empty;
        public List<CloudCatalogModel> Models { get; set; } = new List<CloudCatalogModel>();
        public List<StyleTemplate> StyleTemplates { get; set; } = new List<StyleTemplate>();
        public string DefaultSystemPrompt { get; set; } = string.Empty;
        public List<string> ImageSizes { get; set; } = new List<string>();
        public List<string> AspectRatios { get; set; } = new List<string>();
        public string DefaultImageSize { get; set; } = string.Empty;
        public CloudLimits Limits { get; set; } = new CloudLimits();
    }

    /// <summary>Answer to submitting a generation.</summary>
    public sealed class GenerationAccepted
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int CreditsReserved { get; set; }
    }

    /// <summary>A result image of a generation.</summary>
    public sealed class CloudGenerationImage
    {
        public int Index { get; set; }
        public string Url { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
    }

    /// <summary>Error part of a response or of a failed generation.</summary>
    public sealed class CloudError
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Detail { get; set; }
    }

    /// <summary>State of a generation on the gateway.</summary>
    public sealed class CloudGeneration
    {
        public const string Queued = "queued";
        public const string Running = "running";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";

        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public List<CloudGenerationImage> Images { get; set; } = new List<CloudGenerationImage>();
        public string? Text { get; set; }
        public int CreditsCharged { get; set; }
        public CloudError? Error { get; set; }

        /// <summary>True once the generation can no longer change.</summary>
        public bool IsFinished => Status == Succeeded || Status == Failed || Status == Cancelled;
    }
}
#pragma warning restore CS1591
