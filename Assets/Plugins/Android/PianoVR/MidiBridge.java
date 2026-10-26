package com.pianovr.midi;

import android.content.Context;
import android.media.midi.MidiDevice;
import android.media.midi.MidiDeviceInfo;
import android.media.midi.MidiManager;
import android.media.midi.MidiOutputPort;
import android.media.midi.MidiReceiver;
import android.os.Bundle;
import android.os.Handler;
import android.os.HandlerThread;

import java.io.IOException;
import java.util.HashMap;
import java.util.Map;

/**
 * Puente entre la API MIDI de Android (android.media.midi) y Unity.
 * Abre automáticamente cualquier teclado MIDI conectado por USB (y los ya abiertos por el sistema)
 * y reenvía los mensajes de canal a Unity mediante la interfaz Listener (AndroidJavaProxy en C#).
 */
public class MidiBridge {

    /** Implementada en C# con AndroidJavaProxy. */
    public interface Listener {
        void onMidi(int status, int data1, int data2);
        void onStatus(String message);
    }

    private static MidiBridge instance;

    private final MidiManager manager;
    private final Listener listener;
    private final Handler handler;
    private final HandlerThread thread;
    private final Map<Integer, MidiDevice> openDevices = new HashMap<>();
    private final Map<Integer, MidiOutputPort> openPorts = new HashMap<>();

    private MidiBridge(Context context, Listener listener) {
        this.listener = listener;
        this.manager = (MidiManager) context.getSystemService(Context.MIDI_SERVICE);
        this.thread = new HandlerThread("PianoVR-MIDI");
        this.thread.start();
        this.handler = new Handler(thread.getLooper());
    }

    /** Llamado desde C#. Devuelve true si el sistema tiene servicio MIDI. */
    public static synchronized boolean start(Context context, Listener listener) {
        if (instance != null) instance.stopInternal();
        instance = new MidiBridge(context, listener);
        return instance.startInternal();
    }

    public static synchronized void stop() {
        if (instance != null) {
            instance.stopInternal();
            instance = null;
        }
    }

    private boolean startInternal() {
        if (manager == null) {
            listener.onStatus("Este dispositivo no tiene servicio MIDI");
            return false;
        }

        manager.registerDeviceCallback(new MidiManager.DeviceCallback() {
            @Override
            public void onDeviceAdded(MidiDeviceInfo info) { openDevice(info); }

            @Override
            public void onDeviceRemoved(MidiDeviceInfo info) { closeDevice(info.getId(), true); }
        }, handler);

        MidiDeviceInfo[] devices = manager.getDevices();
        if (devices.length == 0) listener.onStatus("Conecta un teclado MIDI por USB");
        for (MidiDeviceInfo info : devices) openDevice(info);
        return true;
    }

    private void stopInternal() {
        for (Integer id : openDevices.keySet().toArray(new Integer[0])) closeDevice(id, false);
        thread.quitSafely();
    }

    private static String nameOf(MidiDeviceInfo info) {
        Bundle p = info.getProperties();
        String name = p.getString(MidiDeviceInfo.PROPERTY_NAME);
        if (name == null) name = p.getString(MidiDeviceInfo.PROPERTY_PRODUCT);
        return name != null ? name : "Teclado MIDI";
    }

    private void openDevice(final MidiDeviceInfo info) {
        // Solo nos interesan dispositivos que ENVÍAN notas (puertos de salida del dispositivo).
        if (info.getOutputPortCount() == 0 || openDevices.containsKey(info.getId())) return;

        manager.openDevice(info, device -> {
            if (device == null) {
                listener.onStatus("No se pudo abrir " + nameOf(info));
                return;
            }
            MidiOutputPort port = device.openOutputPort(0);
            if (port == null) {
                listener.onStatus("Sin puerto de salida en " + nameOf(info));
                try { device.close(); } catch (IOException ignored) { }
                return;
            }
            port.connect(new Parser(listener));
            openDevices.put(info.getId(), device);
            openPorts.put(info.getId(), port);
            listener.onStatus("Conectado: " + nameOf(info));
        }, handler);
    }

    private void closeDevice(int id, boolean notify) {
        MidiOutputPort port = openPorts.remove(id);
        MidiDevice device = openDevices.remove(id);
        try { if (port != null) port.close(); } catch (IOException ignored) { }
        try { if (device != null) device.close(); } catch (IOException ignored) { }
        if (notify && device != null) listener.onStatus("Teclado MIDI desconectado");
    }

    /** Convierte el flujo de bytes MIDI en mensajes de canal (maneja running status). */
    private static class Parser extends MidiReceiver {
        private final Listener listener;
        private int status = 0;
        private int expected = 0;
        private final int[] data = new int[2];
        private int count = 0;

        Parser(Listener listener) { this.listener = listener; }

        @Override
        public void onSend(byte[] msg, int offset, int length, long timestamp) {
            for (int i = offset; i < offset + length; i++) {
                int b = msg[i] & 0xFF;

                if (b >= 0xF8) continue;          // mensajes de tiempo real (clock, active sensing)
                if (b >= 0xF0) { status = 0; continue; } // sysex / sistema común: se ignoran

                if (b >= 0x80) {
                    status = b;
                    int kind = b & 0xF0;
                    expected = (kind == 0xC0 || kind == 0xD0) ? 1 : 2;
                    count = 0;
                    continue;
                }

                if (status == 0) continue;
                data[count++] = b;
                if (count == expected) {
                    listener.onMidi(status, data[0], expected > 1 ? data[1] : 0);
                    count = 0; // running status: se mantiene el mismo estado
                }
            }
        }
    }
}
