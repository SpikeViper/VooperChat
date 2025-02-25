import DotnetObject = DotNet.DotnetObject;

type MouseMoveService = {
    lastX?: number;
    lastY?: number;
    
    moveListener: (e: MouseEvent) => void;
    
    startMoveListener: () => void;
    stopMoveListener: () => void;
    
    upListener: (e: MouseEvent) => void;
    
    startUpListener: () => void;
    stopUpListener: () => void;
};

export const init = (dotnet: DotnetObject): MouseMoveService => {
    const service = {
        lastX: null,
        lastY: null,
        
        moveListener: async (e: MouseEvent) => {
            document.body.classList.add('no-select');
            
            if (e.clientX === service.lastX && e.clientY === service.lastY) {
                return;
            }
            
            const deltaX = service.lastX ? e.clientX - service.lastX : 0;
            const deltaY = service.lastY ? e.clientY - service.lastY : 0;

            service.lastX = e.clientX;
            service.lastY = e.clientY;

            await dotnet.invokeMethodAsync('NotifyMouseMove', e.clientX, e.clientY, e.pageX, e.pageY, deltaX, deltaY);
        },
        
        startMoveListener: () => {
            document.addEventListener('mousemove', service.moveListener);
        },
        
        stopMoveListener: () => {
            document.removeEventListener('mousemove', service.moveListener);
            service.lastX = null;
            service.lastY = null;
        },
        
        upListener: async (e: MouseEvent) => {
            document.body.classList.remove('no-select');
            await dotnet.invokeMethodAsync('NotifyMouseUp', e.clientX, e.clientY);
        },
        
        startUpListener: () => {
            document.addEventListener('mouseup', service.upListener);
        },
        
        stopUpListener: () => {
            document.removeEventListener('mouseup', service.upListener);
        }
    };
    
    return service;
};