import http from '@ohos.net.http';
import { BusinessError } from '@kit.BasicServicesKit';

class JadeRequest {
  id: string = '';
  method: string = 'GET';
  url: string = '';
  body: string = '';
  token: string = '';
  timeoutMs: number = 15000;
}

class JadeResponse {
  status: number = 0;
  body: string = '';
  error: string = '';
}

// Async requests use Harmony's system TLS trust store. Nothing disables certificate validation.
export class JadeHttp {
  private static pending: Map<string, http.HttpRequest> = new Map();
  private static completed: Map<string, string> = new Map();

  static start(json: string): void {
    const request: JadeRequest = JSON.parse(json) as JadeRequest;
    if (request.url.indexOf('https://') !== 0) {
      const failed = new JadeResponse(); failed.error = 'Harmony network requests require HTTPS.';
      JadeHttp.completed.set(request.id, JSON.stringify(failed)); return;
    }
    const connection = http.createHttp();
    JadeHttp.pending.set(request.id, connection);
    JadeHttp.perform(request, connection);
  }

  private static async perform(request: JadeRequest, connection: http.HttpRequest): Promise<void> {
    const result = new JadeResponse();
    try {
      const headers: Record<string, string> = { 'Accept': 'application/json', 'Content-Type': 'application/json; charset=utf-8' };
      if (request.token.length > 0) headers['X-Auth-Token'] = request.token;
      const options: http.HttpRequestOptions = {
        method: request.method as http.RequestMethod,
        header: headers,
        expectDataType: http.HttpDataType.STRING,
        usingCache: false,
        connectTimeout: request.timeoutMs,
        readTimeout: request.timeoutMs,
        maxLimit: 4 * 1024 * 1024
      };
      if (request.body.length > 0) options.extraData = request.body;
      const response = await connection.request(request.url, options);
      result.status = response.responseCode;
      result.body = typeof response.result === 'string' ? response.result : JSON.stringify(response.result);
    } catch (failure) {
      const error = failure as BusinessError;
      result.error = 'Harmony HTTPS error ' + error.code;
    } finally {
      if (JadeHttp.pending.has(request.id)) {
        JadeHttp.completed.set(request.id, JSON.stringify(result));
        JadeHttp.pending.delete(request.id);
        connection.destroy();
      }
    }
  }

  static take(id: string): string {
    const result = JadeHttp.completed.get(id) ?? '';
    if (result.length > 0) JadeHttp.completed.delete(id);
    return result;
  }

  static cancel(id: string): void {
    const connection = JadeHttp.pending.get(id);
    JadeHttp.pending.delete(id);
    JadeHttp.completed.delete(id);
    if (connection) connection.destroy();
  }
}
