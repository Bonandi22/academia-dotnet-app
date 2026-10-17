// Build de produção (angular.json → fileReplacements). A API é servida na mesma origem: o nginx
// encaminha /api para o contentor da API, por isso não há CORS e a CSP fica em connect-src 'self'.
export const environment = {
  production: true,
  apiBaseUrl: '/api'
};
