// This file can be replaced during build by using the `fileReplacements` array.
// `ng build --prod` replaces `environment.ts` with `environment.prod.ts`.
// The list of file replacements can be found in `angular.json`.

export const environment = {
  production: false,
  cache: {
    logging: false,
  },
  apiUrl: 'http://localhost:5133',
  auth0: {
    domain: 'dev-fgsthtfl2egy63uu.us.auth0.com',
    client_id: '09VPqGTDbVX3Nsgwpf07j8EQ99AHD6kq',
    redirect_uri: 'http://localhost:6200/authorize',
    audience: 'https://void.dbmk2.com',
  },
};

/*
 * For easier debugging in development mode, you can import the following file
 * to ignore zone related error stack frames such as `zone.run`, `zoneDelegate.invokeTask`.
 *
 * This import should be commented out in production mode because it will have a negative impact
 * on performance if an error is thrown.
 */
// import 'zone.js/plugins/zone-error';  // Included with Angular CLI.
