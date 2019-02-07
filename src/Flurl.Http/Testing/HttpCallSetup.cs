using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Flurl.Util;

namespace Flurl.Http.Testing
{
    /// <summary>
    /// A chain of setups to match on a call for a response
    /// </summary>
    public class HttpCallSetup
    {
        private enum SetupMatchType
        {
            Auth,
            Body,
            ContentType,
            Method,
            HeaderInclusive,
            HeaderExclusive,
            Url,
            QueryParamInclusive,
            QueryParamsExclusive,
            QueryParamValueInclusive,
            QueryParamValueExclusive,
            Custom
        }

        private class SetupMatchMetadata
        {
            public SetupMatchType Type { get; set; }
            public string Message { get; set; }
            public string Key { get; internal set; }
        }

        private readonly HttpCallSetupAnd _and;
        private readonly List<Func<HttpCall, bool>> _setupMatchers;
        private readonly Dictionary<SetupMatchType, List<SetupMatchMetadata>> _setupMatchMetadata;

        /// <summary>
        /// The response that should be used if this setup matches a call
        /// </summary>
        internal HttpResponseMessage Response { get; }

        /// <summary>
        /// Creates setup with response to being chaining setup matches
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>        
        internal HttpCallSetup(HttpResponseMessage httpResponseMessage)
        {
            Response = httpResponseMessage;
            _and = new HttpCallSetupAnd(this);
            _setupMatchers = new List<Func<HttpCall, bool>>();
            _setupMatchMetadata = new Dictionary<SetupMatchType, List<SetupMatchMetadata>>();
        }

        /// <summary>
        /// Matches against a url pattern
        /// </summary>
        /// <param name="urlPattern"></param>
        /// <returns></returns>        
        public HttpCallSetupAnd WhenUrlIs(string urlPattern)
        {
            AddSetupMatchMetadata(SetupMatchType.Url, urlPattern);

            return When(c => CallAssertionHelpers.MatchesPattern(c.FlurlRequest.Url, urlPattern));
        }

        /// <summary>
        /// Matches against the request body as json
        /// </summary>
        /// <param name="body"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenJsonRequestBodyIs(object body)
        {
            var bodyJson = FlurlHttp.GlobalSettings.JsonSerializer.Serialize(body);
            AddSetupMatchMetadata(SetupMatchType.Body, bodyJson);

            return When(c => CallAssertionHelpers.MatchesPattern(c.RequestBody, bodyJson));
        }

        /// <summary>
        /// Matches against the http method. Can only be matched on once.
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenRequestMethodIs(HttpMethod method)
        {
            AddSetupMatchMetadata(SetupMatchType.Method, $"Method: {method}");

            return When(c => c.Request.Method == method);
        }

        /// <summary>
        /// Matches against the query params being present, does not check their values
        /// </summary>
        /// <param name="names"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenQueryParamsArePresent(params string[] names)
        {
            foreach (var name in names)
            {
                AddSetupMatchMetadata(SetupMatchType.QueryParamInclusive, $"query parameter {name}", name);
                When(c => c.FlurlRequest.Url.QueryParams.Any(q => q.Name == name));
            }

            return _and;
        }

        /// <summary>
        /// Matches when the query params are not present
        /// </summary>
        /// <param name="names"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenQueryParamsAreNotPresent(params string[] names)
        {
            foreach (var name in names)
            {
                AddSetupMatchMetadata(SetupMatchType.QueryParamInclusive, $"no query parameter {name}", name);
                WhenNot(c => c.FlurlRequest.Url.QueryParams.Any(q => q.Name == name));
            }

            return _and;
        }

        /// <summary>
        /// Matches a query param name and value
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value">supports collection of values</param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenQueryParamValueIsPresent(string name, object value)
        {
            if (!(value is string) && value is IEnumerable en)
            {
                foreach (var val in en)
                {
                    WhenQueryParamValueIsPresent(name, val);
                }

                return _and;
            }

            AddSetupMatchMetadata(SetupMatchType.QueryParamValueInclusive, $"query parameter {name}={value}", name);

            return When(c => c.FlurlRequest.Url.QueryParams.Any(qp => CallAssertionHelpers.QueryParamMatches(qp, name, value)));
        }

        /// <summary>
        /// Matches when query param name and value are not present
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value">supports collection of values</param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenQueryParamValueIsNotPresent(string name, object value)
        {
            if (!(value is string) && value is IEnumerable en)
            {
                foreach (var val in en)
                {
                    WhenQueryParamValueIsNotPresent(name, val);
                }
                return _and;
            }

            AddSetupMatchMetadata(SetupMatchType.QueryParamValueExclusive, $"no query parameter {name}={value}", name);

            return WhenNot(c => c.FlurlRequest.Url.QueryParams.Any(qp => CallAssertionHelpers.QueryParamMatches(qp, name, value)));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenQueryParamValuesMatch(object values)
        {
            return values.ToKeyValuePairs().Select(kv => WhenQueryParamValueIsPresent(kv.Key, kv.Value)).LastOrDefault() ?? _and;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenQueryParamValuesDoNotMatch(object values)
        {
            return values.ToKeyValuePairs().Select(kv => WhenQueryParamValueIsNotPresent(kv.Key, kv.Value)).LastOrDefault() ?? _and;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenContentTypeIs(string contentType)
        {
            AddSetupMatchMetadata(SetupMatchType.ContentType, $"content type {contentType}", contentType);

            return When(c => c.Request.Content?.Headers?.ContentType?.MediaType == contentType);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenHeaderIsPresent(string name, string valuePattern = "*")
        {
            AddSetupMatchMetadata(SetupMatchType.HeaderInclusive, $"header {name}: {valuePattern}", name);

            return When(c =>
            {
                var val = c.Request.GetHeaderValue(name);
                return val != null && CallAssertionHelpers.MatchesPattern(val, valuePattern);
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenHeaderIsNotPresent(string name, string valuePattern = "*")
        {
            AddSetupMatchMetadata(SetupMatchType.HeaderExclusive, $"no header {name}: {valuePattern}", name);

            return WhenNot(c =>
            {
                var val = c.Request.GetHeaderValue(name);
                return val != null && CallAssertionHelpers.MatchesPattern(val, valuePattern);
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenBasicAuthIsUsed(string username, string password)
        {
            AddSetupMatchMetadata(SetupMatchType.Auth, $"basic auth credentials {username}/{password}");

            var value = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            return When(c => c.Request.Headers.Authorization?.Scheme == "Basic"
                && c.Request.Headers.Authorization?.Parameter == value);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenOAuthBearerToken(string token)
        {
            AddSetupMatchMetadata(SetupMatchType.Auth, $"OAuth bearer token {token}");

            return When(c => c.Request.Headers.Authorization?.Scheme == "Bearer"
                   && c.Request.Headers.Authorization?.Parameter == token);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd WhenNot(Func<HttpCall, bool> match)
        {
            AddSetupMatchMetadata(SetupMatchType.Custom, "WhenNot");

            return When(c => !match(c));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="call"></param>
        /// <returns></returns>  
        public HttpCallSetupAnd When(Func<HttpCall, bool> match)
        {
            AddSetupMatchMetadata(SetupMatchType.Custom, "When");

            _setupMatchers.Add(match);

            return _and;
        }

        /// <summary>
        /// Adds setup match metadata validating whether it makes sense. 
        /// Throws exceptions on invalid match addition attempt.
        /// </summary>
        /// <param name="call">call to match against</param>
        /// <exception cref="InvalidOperationException">The setup match is not valid</exception> 
        private void AddSetupMatchMetadata(SetupMatchType setupType, string message, string key = null)
        {
            SetupMatchType GetContradictoryType(SetupMatchType st)
            {
                switch (st)
                {
                    case SetupMatchType.HeaderExclusive:
                        return SetupMatchType.HeaderInclusive;
                    case SetupMatchType.HeaderInclusive:
                        return SetupMatchType.HeaderExclusive;
                    case SetupMatchType.QueryParamsExclusive:
                        return SetupMatchType.QueryParamInclusive;
                    case SetupMatchType.QueryParamInclusive:
                        return SetupMatchType.QueryParamsExclusive;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(st), st.ToString());
                }
            }

            if (!_setupMatchMetadata.ContainsKey(setupType))
            {
                _setupMatchMetadata[setupType] = new List<SetupMatchMetadata>();
            }

            var setupMetadatas = _setupMatchMetadata[setupType];

            switch (setupType)
            {
                case SetupMatchType.Body:
                case SetupMatchType.ContentType:
                case SetupMatchType.Url:
                case SetupMatchType.Auth:
                case SetupMatchType.Method:
                    if (setupMetadatas.Any())
                    {
                        throw new InvalidOperationException($"Cannot setup multiple {setupType}. Previous setup: {setupMetadatas.First().Message}");
                    }
                    break;
                case SetupMatchType.HeaderExclusive:
                case SetupMatchType.HeaderInclusive:
                case SetupMatchType.QueryParamsExclusive:
                case SetupMatchType.QueryParamInclusive:
                    var duplicate = setupMetadatas.FirstOrDefault(x => x.Key == key);
                    if (duplicate != null)
                    {
                        throw new InvalidOperationException($"Cannot setup multiple {setupType} for the same {key}. Previous setup: {duplicate.Message}");
                    }

                    var contradictoryType = GetContradictoryType(setupType);
                    if (_setupMatchMetadata.TryGetValue(GetContradictoryType(setupType), out var possibleContradictions))
                    {
                        var contradiction = possibleContradictions.FirstOrDefault(x => x.Key == key);
                        if (contradiction != null)
                        {
                            throw new InvalidOperationException($"Cannot setup both {setupType} and {contradictoryType} for {key}. Previous setup: {contradiction.Message}");
                        }
                    }
                    break;
                case SetupMatchType.QueryParamValueExclusive:
                case SetupMatchType.QueryParamValueInclusive:
                    break;
            }

            setupMetadatas.Add(new SetupMatchMetadata
            {
                Key = key,
                Message = message,
                Type = setupType
            });
        }

        /// <summary>
        /// Returns true if the call matches against this setup
        /// </summary>
        /// <param name="call">call to match against</param>
        /// <returns></returns>
        internal bool Matches(HttpCall call)
        {
            return _setupMatchers.All(m => m(call));
        }
    }
}