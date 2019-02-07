namespace Flurl.Http.Testing
{
    /// <summary>
    /// Allows for fluent chaining of setup calls
    /// </summary>
    public class HttpCallSetupAnd
    {
        private readonly HttpCallSetup _setup;
        internal HttpCallSetupAnd(HttpCallSetup setup)
        {
            _setup = setup;
        }

        /// <summary>
        /// The current call setup to chain onto
        /// </summary>
        public HttpCallSetup And => _setup;
    }
}
