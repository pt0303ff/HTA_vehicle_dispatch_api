//產生隨機 Nonce。
//保存 Nonce 的使用狀態。
//限制 120 秒有效期限。
//確保同一 Nonce 只能消耗一次。



using System;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace vehicle_dispatch_api.Services.Auth
{
    public sealed class DpopNonceService
    {
        private readonly IMemoryCache _cache;
        private readonly object _syncLock = new();

        private static readonly TimeSpan NonceLifetime =
            TimeSpan.FromSeconds(120);

        private const string CachePrefix = "dpop:nonce:";

        public DpopNonceService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public string CreateNonce(Guid phoneId)
        {
            if (phoneId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Phone ID 不可為空");
            }

            // 產生 32 Bytes 密碼學安全亂數
            var nonceBytes =
                RandomNumberGenerator.GetBytes(32);

            var nonce = Convert.ToBase64String(nonceBytes);

            var state = new NonceState
            {
                PhoneId = phoneId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(NonceLifetime)
            };

            lock (_syncLock)
            {
                _cache.Set(
                    CachePrefix + nonce,
                    state,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow =
                            NonceLifetime
                    });
            }

            return nonce;
        }

        public bool TryConsumeNonce(
            string nonce,
            Guid phoneId)
        {
            if (string.IsNullOrWhiteSpace(nonce) ||
                phoneId == Guid.Empty)
            {
                return false;
            }

            var key = CachePrefix + nonce;

            lock (_syncLock)
            {
                if (!_cache.TryGetValue(
                    key,
                    out NonceState? state))
                {
                    return false;
                }

                if (state == null ||
                    state.PhoneId != phoneId ||
                    state.ExpiresAt <= DateTime.UtcNow)
                {
                    _cache.Remove(key);
                    return false;
                }

                // 一次性使用
                _cache.Remove(key);

                return true;
            }
        }

        private sealed class NonceState
        {
            public Guid PhoneId { get; set; }

            public DateTime CreatedAt { get; set; }

            public DateTime ExpiresAt { get; set; }
        }
    }
}

