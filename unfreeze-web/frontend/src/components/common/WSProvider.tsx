// Unfreeze — WebSocket Provider
// Zustand 是全局 store，这里仅作为可选的初始化包装器。
// 应用挂载时建立 WS 连接；卸载时清理。

import { useEffect, type ReactNode } from 'react';
import { useSessionStore } from '../../stores/sessionStore';

interface WSProviderProps {
  children: ReactNode;
  autoConnect?: boolean;
}

export function WSProvider({ children, autoConnect = false }: WSProviderProps) {
  const connectWebSocket = useSessionStore((s) => s.connectWebSocket);
  const disconnect = useSessionStore((s) => s.disconnect);

  useEffect(() => {
    if (autoConnect) {
      connectWebSocket();
    }
    return () => {
      disconnect();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return <>{children}</>;
}
