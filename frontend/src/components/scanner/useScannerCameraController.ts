import { useEffect, useRef, useState, type MutableRefObject } from 'react';
import { decodeBarcodeFromVideoFrame } from '../../utils/cameraBarcodeDecoder';
import { playScannerBeep } from './scannerAudioAndGs1';

export function useScannerCameraController(triggerRef: MutableRefObject<(rawCode: string) => Promise<void>>) {
  const [cameraActive, setCameraActive] = useState(false);
  const [isCameraLoading, setIsCameraLoading] = useState(false);
  const [hasActiveVideoStream, setHasActiveVideoStream] = useState(false);
  const [cameraError, setCameraError] = useState(false);
  const [cameraFlashSuccess, setCameraFlashSuccess] = useState(false);
  const [cameraNotice, setCameraNotice] = useState('');

  const videoRef = useRef<HTMLVideoElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const timerRef = useRef<number | null>(null);
  const probeTimerRef = useRef<number | null>(null);
  const lastCameraScanRef = useRef<{ code: string; timeMs: number }>({ code: '', timeMs: 0 });

  const stopCamera = () => {
    if (timerRef.current !== null) {
      window.clearInterval(timerRef.current);
      timerRef.current = null;
    }
    if (probeTimerRef.current !== null) {
      window.clearTimeout(probeTimerRef.current);
      probeTimerRef.current = null;
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach(track => track.stop());
      streamRef.current = null;
    }
    if (videoRef.current) {
      videoRef.current.srcObject = null;
    }
    setCameraActive(false);
    setIsCameraLoading(false);
    setHasActiveVideoStream(false);
    setCameraError(false);
  };

  useEffect(() => {
    return () => {
      stopCamera();
    };
  }, []);

  const startCameraStream = async () => {
    if (timerRef.current !== null) {
      window.clearInterval(timerRef.current);
      timerRef.current = null;
    }
    if (probeTimerRef.current !== null) {
      window.clearTimeout(probeTimerRef.current);
      probeTimerRef.current = null;
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach(track => track.stop());
      streamRef.current = null;
    }

    setCameraActive(true);
    setIsCameraLoading(true);
    setHasActiveVideoStream(false);
    setCameraError(false);
    setCameraNotice('');

    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setIsCameraLoading(false);
      setHasActiveVideoStream(false);
      setCameraError(true);
      setCameraNotice(
        'Trình duyệt hiện tại chưa kết nối Camera trực tiếp. Bạn có thể nhập mã hoặc dùng súng quét USB/Bluetooth ngay tại ô bên trên.',
      );
      return;
    }

    probeTimerRef.current = window.setTimeout(() => {
      const videoEl = videoRef.current;
      if (!videoEl || videoEl.readyState < 2 || videoEl.videoWidth <= 0) {
        setHasActiveVideoStream(false);
        setCameraError(true);
        setIsCameraLoading(false);
      }
    }, 3000);

    try {
      let stream: MediaStream;
      try {
        stream = await navigator.mediaDevices.getUserMedia({
          video: {
            facingMode: { ideal: 'environment' },
            width: { ideal: 1280, min: 640 },
            height: { ideal: 720, min: 480 },
          },
        });
      } catch {
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: 'environment' },
        });
      }
      streamRef.current = stream;

      const hasLiveTrack = stream.getVideoTracks().some(t => t.readyState === 'live' && t.enabled);
      if (!hasLiveTrack) {
        if (probeTimerRef.current !== null) {
          window.clearTimeout(probeTimerRef.current);
          probeTimerRef.current = null;
        }
        setIsCameraLoading(false);
        setHasActiveVideoStream(false);
        setCameraError(true);
        setCameraNotice(
          'Không tìm thấy luồng video từ Camera vật lý. Bạn có thể nhập mã trực tiếp, dùng súng quét USB/Bluetooth hoặc điền mã mẫu.',
        );
        return;
      }

      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play().catch(() => {});
      }

      if (probeTimerRef.current !== null) {
        window.clearTimeout(probeTimerRef.current);
      }
      probeTimerRef.current = window.setTimeout(() => {
        const videoEl = videoRef.current;
        if (videoEl && videoEl.readyState >= 2 && videoEl.videoWidth > 0) {
          setHasActiveVideoStream(true);
          setCameraError(false);
          setIsCameraLoading(false);
        } else {
          setHasActiveVideoStream(false);
          setCameraError(true);
          setIsCameraLoading(false);
          setCameraNotice(
            'Thiết bị chưa cấp quyền hoặc không có Camera vật lý. Bạn có thể nhập mã trực tiếp, dùng súng quét USB/Bluetooth hoặc điền mã mẫu.',
          );
        }
      }, 450);

      let decodingFrame = false;
      timerRef.current = window.setInterval(async () => {
        if (decodingFrame) return;
        const videoEl = videoRef.current;
        const canvasEl = canvasRef.current;
        if (!videoEl || !canvasEl || videoEl.readyState < 2 || videoEl.videoWidth <= 0) return;
        decodingFrame = true;
        try {
          const hit = await decodeBarcodeFromVideoFrame(videoEl, canvasEl);
          if (hit?.code) {
            const now = Date.now();
            if (lastCameraScanRef.current.code === hit.code && now - lastCameraScanRef.current.timeMs < 2000) {
              return;
            }
            lastCameraScanRef.current = { code: hit.code, timeMs: now };
            playScannerBeep();
            setCameraFlashSuccess(true);
            window.setTimeout(() => setCameraFlashSuccess(false), 550);
            await triggerRef.current(hit.code);
          }
        } catch {
          // Bỏ qua lỗi giải mã từng khung hình
        } finally {
          decodingFrame = false;
        }
      }, 65);
    } catch {
      setIsCameraLoading(false);
      setHasActiveVideoStream(false);
      setCameraError(true);
      setCameraNotice(
        'Thiết bị chưa cấp quyền hoặc không có Camera vật lý. Bạn có thể nhập mã trực tiếp, dùng súng quét USB/Bluetooth hoặc điền mã mẫu.',
      );
    }
  };

  const toggleCamera = async () => {
    if (cameraActive) {
      stopCamera();
      return;
    }
    await startCameraStream();
  };

  const onVideoLoadedData = () => {
    if (videoRef.current && videoRef.current.videoWidth > 0 && videoRef.current.readyState >= 2) {
      setHasActiveVideoStream(true);
      setCameraError(false);
      setIsCameraLoading(false);
    }
  };

  const isLiveVideo = cameraActive && hasActiveVideoStream && !cameraError;
  const isDisconnectedCamera = cameraActive && !isCameraLoading && (!hasActiveVideoStream || cameraError);

  return {
    cameraActive,
    isCameraLoading,
    cameraFlashSuccess,
    cameraNotice,
    setCameraNotice,
    videoRef,
    canvasRef,
    startCameraStream,
    toggleCamera,
    onVideoLoadedData,
    isLiveVideo,
    isDisconnectedCamera,
  };
}
