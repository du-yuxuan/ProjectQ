// Unfreeze — 路由根组件

import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { Layout } from './components/common/Layout';
import { WSProvider } from './components/common/WSProvider';
import { LandingPage } from './pages/LandingPage';
import { SessionPage } from './pages/SessionPage';
import { ReportPage } from './pages/ReportPage';
import { ProfilePage } from './pages/ProfilePage';
import { CredentialsPage } from './pages/CredentialsPage';

export function App() {
  return (
    <BrowserRouter>
      <WSProvider>
        <Layout>
          <Routes>
            <Route path="/" element={<LandingPage />} />
            <Route path="/session" element={<SessionPage />} />
            <Route path="/report" element={<ReportPage />} />
            <Route path="/profile" element={<ProfilePage />} />
            <Route path="/credentials" element={<CredentialsPage />} />
          </Routes>
        </Layout>
      </WSProvider>
    </BrowserRouter>
  );
}
