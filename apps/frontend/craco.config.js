// craco.config.js
const path = require("path");
const webpack = require("webpack");
require("dotenv").config();

// Environment variable overrides
const config = {
  enableHealthCheck: process.env.ENABLE_HEALTH_CHECK === "true",
};

const frontendModeRaw = (process.env.REACT_APP_FRONTEND_MODE || "")
  .trim()
  .toLowerCase();
let frontendMode = frontendModeRaw;
if (!frontendMode) {
  if (process.env.NODE_ENV === "production") {
    throw new Error(
      'REACT_APP_FRONTEND_MODE is required for production builds. Use yarn build / yarn build:production (production) or yarn build:prototype.',
    );
  }
  frontendMode = process.env.NODE_ENV === "test" ? "test" : "prototype";
}
if (!["prototype", "production", "test"].includes(frontendMode)) {
  throw new Error(
    `Invalid REACT_APP_FRONTEND_MODE="${process.env.REACT_APP_FRONTEND_MODE}". Expected prototype|production|test.`,
  );
}

if (frontendMode === "production") {
  process.env.GENERATE_SOURCEMAP = process.env.GENERATE_SOURCEMAP || "false";
}

function makeDevServerV5Compatible(devServerConfig) {
  const {
    https,
    onAfterSetupMiddleware,
    onBeforeSetupMiddleware,
    onListening,
    setupMiddlewares,
    ...compatibleConfig
  } = devServerConfig;

  compatibleConfig.server =
    typeof https === "object"
      ? { type: "https", options: https }
      : https
        ? "https"
        : "http";
  compatibleConfig.headers = {
    ...compatibleConfig.headers,
    "Cross-Origin-Resource-Policy": "same-origin",
  };

  if (onBeforeSetupMiddleware || setupMiddlewares) {
    compatibleConfig.setupMiddlewares = (middlewares, devServer) => {
      if (onBeforeSetupMiddleware) {
        onBeforeSetupMiddleware(devServer);
      }

      return setupMiddlewares
        ? setupMiddlewares(middlewares, devServer)
        : middlewares;
    };
  }

  compatibleConfig.onListening = (devServer) => {
    devServer.close ??= (callback) => devServer.stopCallback(callback);

    if (onListening) {
      onListening(devServer);
    }
    if (onAfterSetupMiddleware) {
      onAfterSetupMiddleware(devServer);
    }
  };

  return compatibleConfig;
}

// Conditionally load health check modules only if enabled
let WebpackHealthPlugin;
let setupHealthEndpoints;
let healthPluginInstance;

if (config.enableHealthCheck) {
  WebpackHealthPlugin = require("./plugins/health-check/webpack-health-plugin");
  setupHealthEndpoints = require("./plugins/health-check/health-endpoints");
  healthPluginInstance = new WebpackHealthPlugin();
}

let webpackConfig = {
  eslint: {
    configure: {
      extends: ["plugin:react-hooks/recommended"],
      rules: {
        "react-hooks/rules-of-hooks": "error",
        "react-hooks/exhaustive-deps": "warn",
      },
    },
  },
  jest: {
    configure: (jestConfig) => {
      jestConfig.moduleNameMapper = {
        ...jestConfig.moduleNameMapper,
        "^@/(.*)$": "<rootDir>/src/$1",
        "^@cocinaris/api-client$":
          "<rootDir>/src/generated/api-client/index.ts",
        "^@cocinaris/api-client/(.*)$":
          "<rootDir>/src/generated/api-client/$1",
        "^react-router-dom$": "<rootDir>/node_modules/react-router-dom/dist/index.js",
        "^react-router/dom$": "<rootDir>/node_modules/react-router/dist/development/dom-export.js",
        "^react-router$": "<rootDir>/node_modules/react-router/dist/development/index.js",
      };
      jestConfig.transformIgnorePatterns = [
        "[/\\\\]node_modules[/\\\\](?!(react-router|react-router-dom|@remix-run|openapi-fetch|openapi-typescript-helpers)[/\\\\]).+",
      ];
      return jestConfig;
    },
  },
  webpack: {
    alias: {
      '@': path.resolve(__dirname, 'src'),
      '@cocinaris/api-client': path.resolve(
        __dirname,
        'src/generated/api-client/index.ts',
      ),
    },
    configure: (webpackConfig) => {

      // Add ignored patterns to reduce watched directories
        webpackConfig.watchOptions = {
          ...webpackConfig.watchOptions,
          ignored: [
            '**/node_modules/**',
            '**/.git/**',
            '**/build/**',
            '**/dist/**',
            '**/coverage/**',
            '**/public/**',
        ],
      };

      if (frontendMode === "production") {
        webpackConfig.plugins.push(
          new webpack.NormalModuleReplacementPlugin(
            /[\\/]components[\\/]ScenarioBar([\\/]index)?$/,
            path.resolve(__dirname, "src/components/runtime/ScenarioBar.production.tsx"),
          ),
          new webpack.NormalModuleReplacementPlugin(
            /[\\/]app[\\/]runtime[\\/]createPrototypeRuntime$/,
            path.resolve(
              __dirname,
              "src/app/runtime/createPrototypeRuntime.production.ts",
            ),
          ),
          new webpack.NormalModuleReplacementPlugin(
            /[\\/]app[\\/]PrototypeApp$/,
            path.resolve(__dirname, "src/app/PrototypeApp.production.ts"),
          ),
        );
      }

      // Add health check plugin to webpack if enabled
      if (config.enableHealthCheck && healthPluginInstance) {
        webpackConfig.plugins.push(healthPluginInstance);
      }
      return webpackConfig;
    },
  },
};

webpackConfig.devServer = (devServerConfig) => {
  // Add health check endpoints if enabled
  if (config.enableHealthCheck && setupHealthEndpoints && healthPluginInstance) {
    const originalSetupMiddlewares = devServerConfig.setupMiddlewares;

    devServerConfig.setupMiddlewares = (middlewares, devServer) => {
      // Call original setup if exists
      if (originalSetupMiddlewares) {
        middlewares = originalSetupMiddlewares(middlewares, devServer);
      }

      // Setup health endpoints
      setupHealthEndpoints(devServer, healthPluginInstance);

      return middlewares;
    };
  }

  return devServerConfig;
};

const configureDevServer = webpackConfig.devServer;
webpackConfig.devServer = (devServerConfig) =>
  makeDevServerV5Compatible(configureDevServer(devServerConfig));

module.exports = webpackConfig;
