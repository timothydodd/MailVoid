export const environment = {
  production: true,
  cache: {
    logging: false,
  },
  apiUrl: '${API_URL}',
  auth0: {
    domain: '${AUTH_DOMAIN}',
    client_id: '${AUTH_ID}',
    redirect_uri: '${AUTH_REDIRECT}',
    audience: '${AUTH_AUDIENCE}',
  },
};
