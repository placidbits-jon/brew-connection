const target = process.env.API_HTTPS || process.env.API_HTTP;

if (!target) {
  throw new Error('Set API_HTTPS or API_HTTP before starting the Angular development server.');
}

module.exports = {
  '/api': {
    target,
    secure: false,
    changeOrigin: true,
  },
};
