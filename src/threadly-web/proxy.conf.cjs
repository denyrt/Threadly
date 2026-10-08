const target = process.env.API_HTTPS || process.env.API_HTTP || 'https://localhost:7149';
const options = {
  target,
  secure: false,
  changeOrigin: true,
  configure(proxy) {
    proxy.on('proxyReq', (proxyRequest, request) => {
      proxyRequest.setHeader('X-Forwarded-For', request.socket.remoteAddress);
      proxyRequest.setHeader('X-Forwarded-Proto', request.socket.encrypted ? 'https' : 'http');
    });
  },
};

module.exports = {
  '/api': options,
  '/api/**': options,
  '/hubs/comments': { ...options, ws: true },
  '/hubs/comments/**': { ...options, ws: true },
};
