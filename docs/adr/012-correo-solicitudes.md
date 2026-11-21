# ADR-012: Aviso por correo de las solicitudes del servicio

- **Estado:** Aceptado
- **Fecha:** 21/11/2026
- **Drivers:** DRV-003

## Contexto

Las solicitudes de demostración, cotización o cambio de plan (RF-029, HU-028) se guardaban en `solicitud_servicio` y nadie del proveedor se enteraba hasta consultar la base. El DOC-06 dejaba fuera los correos (CR-001 rechazado): las notificaciones a los usuarios son internas, en la campana de la aplicación. La solicitud del servicio es distinta: el destinatario es el equipo comercial del proveedor y no un usuario de la institución.

Alternativas consideradas:

1. Seguir sin avisos y revisar las solicitudes a mano.
2. Un servicio de correo transaccional externo (SendGrid, Amazon SES) con su SDK.
3. SMTP con MailKit hacia una cuenta del proveedor, enviado por la misma API.

## Decisión

Opción 3. Después de guardar la solicitud, la API pone en cola un aviso (`IAvisoSolicitudes`) y un servicio en segundo plano lo envía por SMTP con MailKit (`EnviadorCorreoSmtp`) al destino `Proveedor:CorreoSolicitudes`, con un reintento. El correo lleva el código SOL-######, el tipo, el plan, la institución, el contacto y el mensaje. La configuración va en `Smtp:Servidor`, `Smtp:Puerto`, `Smtp:Seguridad` (StartTls, Tls o Ninguna), `Smtp:Usuario`, `Smtp:Contrasena` y `Smtp:Remitente`; el usuario y la contraseña de aplicación solo en user-secrets o variables de entorno, nunca en el repositorio. Sin servidor, remitente o destino no se intenta enviar (por ejemplo, en la CI). En local se prueba con `smtp4dev` del `docker-compose.yml`.

CR-001 sigue rechazado: SITBIO no envía correos a los usuarios de las instituciones.

## Consecuencias

- El proveedor recibe cada solicitud al momento, sin cambiar la respuesta del endpoint (API-SERV-SOL).
- Si el envío falla, la solicitud queda guardada y se registra el error sin datos personales; no hay reintento después del segundo intento.
- No se agrega otro contenedor ni otro servicio en la nube (ADR-008); la API necesita salida al puerto SMTP del proveedor de correo.
- Cambiar de proveedor de correo es solo configuración.
