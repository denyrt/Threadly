const target = process.env.API_HTTPS || process.env.API_HTTP || 'https://localhost:7149';
const options = { target, secure: false, changeOrigin: true };

module.exports = {
  '/api': options,
  '/api/**': options,
  '/hubs/comments': { ...options, ws: true },
  '/hubs/comments/**': { ...options, ws: true },
};
