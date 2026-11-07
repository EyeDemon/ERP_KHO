import React from 'react';
import PlatformControlCenterLayout from './layouts/PlatformControlCenterLayout';
import RoutesImpl from './routes/AppRoutes';

export const PlatformControlCenter = React.lazy(() => import('./pages/PlatformControlCenter'));
export { PlatformControlCenterLayout };

// Mounts /platform and /platform/* wrapped in PlatformControlCenterLayout via RoutesImpl
export default RoutesImpl;
