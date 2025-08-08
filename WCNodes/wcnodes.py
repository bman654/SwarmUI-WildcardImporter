import torch, comfy
import numpy as np
from scipy import ndimage

class WCCompositeMask:
    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "mask_a": ("MASK",),
                "mask_b": ("MASK",),
                "op": (["max", "min"],),
            }
        }

    CATEGORY = "WC/masks"
    RETURN_TYPES = ("MASK",)
    FUNCTION = "combine"
    DESCRIPTION = "Combines two masks using the specified operator."

    def combine(self, mask_a, mask_b, op):
        output = mask_b.reshape((-1, mask_b.shape[-2], mask_b.shape[-1])).clone()
        source = mask_a.reshape((-1, mask_a.shape[-2], mask_a.shape[-1]))
    
        left, top = (0, 0,)
        right, bottom = (min(left + source.shape[-1], mask_b.shape[-1]), min(top + source.shape[-2], mask_b.shape[-2]))
        visible_width, visible_height = (right - left, bottom - top,)
    
        source_portion = source[:, :visible_height, :visible_width]
        destination_portion = output[:, top:bottom, left:right]
    
        if op == "max":
            output[:, top:bottom, left:right] = torch.fmax(destination_portion, source_portion)
        elif op == "min":
            output[:, top:bottom, left:right] = torch.fmin(destination_portion, source_portion)
    
        return (output,)

# Adapted from SwarmMaskBounds
class WCMaskBounds:
    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "mask": ("MASK",),
                "grow": ("INT", {"default": 0, "min": 0, "max": 1024, "tooltip": "Number of pixels to grow the mask by."}),
            },
            "optional": {
                "aspect_x": ("INT", {"default": 0, "min": 0, "max": 4096, "tooltip": "An X width value, used to indicate a target aspect ratio. 0 to allow any aspect."}),
                "aspect_y": ("INT", {"default": 0, "min": 0, "max": 4096, "tooltip": "A Y height value, used to indicate a target aspect ratio. 0 to allow any aspect."}),
                "dynamic": ("BOOLEAN", {"default": False, "tooltip": "If true, the aspect_x/y are only used to indicate overall minimum target pixel count the actual resolution will be chosen intelligently based upon mask size."}),
            }
        }

    CATEGORY = "WC/masks"
    RETURN_TYPES = ("INT", "INT", "INT", "INT")
    RETURN_NAMES = ("x", "y", "width", "height")
    FUNCTION = "get_bounds"
    DESCRIPTION = "Returns the bounding box of the mask (as pixel coordinates x,y,width,height), optionally grown by the number of pixels specified in 'grow' and then optionally adjusted for aspect ratio."

    def get_bounds(self, mask, grow, aspect_x=0, aspect_y=0, dynamic=False):
        if len(mask.shape) == 3:
            mask = mask[0]
        sum_x = (torch.sum(mask, dim=0) != 0).to(dtype=torch.int)
        sum_y = (torch.sum(mask, dim=1) != 0).to(dtype=torch.int)
        def getval(arr, direction):
            val = torch.argmax(arr).item()
            val += grow * direction
            val = max(0, min(val, arr.shape[0] - 1))
            return val
        x_start = getval(sum_x, -1)
        x_end = mask.shape[1] - getval(sum_x.flip(0), -1)
        y_start = getval(sum_y, -1)
        y_end = mask.shape[0] - getval(sum_y.flip(0), -1)
        if aspect_x > 0 and aspect_y > 0:
            input_aspect = aspect_x / aspect_y
            width = x_end - x_start
            height = y_end - y_start
            actual_aspect = width / height
            if dynamic:
                allowed_aspect_ratios = [1, 4/3, 3/2, 8/5, 16/9, 21/9, 3/4, 2/3, 5/8, 9/16, 9/21, input_aspect]
                input_aspect = min(allowed_aspect_ratios, key=lambda x: abs(x - actual_aspect))
            if actual_aspect > input_aspect:
                desired_height = width / input_aspect
                y_start = max(0, y_start - (desired_height - height) / 2)
                y_end = min(mask.shape[0], y_start + desired_height)
            else:
                desired_width = height * input_aspect
                x_start = max(0, x_start - (desired_width - width) / 2)
                x_end = min(mask.shape[1], x_start + desired_width)
        return (int(x_start), int(y_start), int(x_end - x_start), int(y_end - y_start))


class WCSkipIfMaskEmpty:
    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "mask": ("MASK",),
                "image_if_empty": ("IMAGE",{"lazy": True}),
                "image_if_not_empty": ("IMAGE",{"lazy": True}),
            }
        }

    CATEGORY = "WC/masks"
    RETURN_TYPES = ("IMAGE",)
    FUNCTION = "route"
    DESCRIPTION = "If the mask is empty, returns the 'image_if_empty' image. Otherwise, returns the 'image_if_not_empty' image.  Only evaluates the input image that is going to be returned."

    def check_lazy_status(self, mask, image_if_empty, image_if_not_empty):
        mask_max = mask.max()
        if mask_max == 0 and image_if_empty is None:
            return ["image_if_empty"]
        elif mask_max > 0 and image_if_not_empty is None:
            return ["image_if_not_empty"]
        return []
    
    def route(self, mask, image_if_empty, image_if_not_empty):
        mask_max = mask.max()
        if mask_max == 0:
            return (image_if_empty,)
        else:
            return (image_if_not_empty,)


class WCSeparateMaskComponents:
    """
    Separates a mask into multiple contiguous components.
    """
    def __init__(self):
        pass

    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "mask": ("MASK",),
                "sort_order": (["left-right", "right-left", "top-bottom", "bottom-top", "largest-smallest", "smallest-largest"], ),
                "index": ("INT", { "default": 0, "min": 0, "max": 256, "step": 1 }),
            },
            "optional": {
                "orig_mask": ("MASK",),
            }
        }

    RETURN_TYPES = ("MASK",)
    RETURN_NAMES = ("mask",)
    FUNCTION = "separate"

    CATEGORY = "WC/masks"

    def separate(self, mask, sort_order, index, orig_mask=None):
        """
        Separates a mask into contiguous components and returns the component at the specified index.
        
        Args:
            mask: Input mask tensor
            sort_order: How to sort the found components
            index: Which component to return (0-based)
            orig_mask: Optional original mask to use for output values
        
        Returns:
            A mask with only the selected component
        """
        # Use original mask values if provided, otherwise use input mask
        source_mask = orig_mask if orig_mask is not None else mask
        
        # Get the first batch item (assuming single batch for mask processing)
        if len(mask.shape) == 3:
            mask_np = mask[0].cpu().numpy()
            source_np = source_mask[0].cpu().numpy() if len(source_mask.shape) == 3 else source_mask.cpu().numpy()
        else:
            mask_np = mask.cpu().numpy()
            source_np = source_mask.cpu().numpy()
        
        # Create binary mask (values > 0)
        binary_mask = mask_np > 0
        
        # Create 8-connectivity structure (includes diagonals)
        structure = np.ones((3, 3), dtype=bool)
        
        # Find connected components using scipy with 8-connectivity
        labeled_array, num_features = ndimage.label(binary_mask, structure=structure)
        
        if num_features == 0:
            # No components found, return empty mask
            return (torch.zeros_like(mask),)
        
        # Get component information for sorting
        components_info = []
        for i in range(1, num_features + 1):  # Labels start from 1
            component_mask = labeled_array == i
            
            # Calculate properties for sorting
            coords = np.where(component_mask)
            if len(coords[0]) == 0:
                continue
                
            # Calculate bounding box and center
            min_y, max_y = coords[0].min(), coords[0].max()
            min_x, max_x = coords[1].min(), coords[1].max()
            center_y = (min_y + max_y) / 2
            center_x = (min_x + max_x) / 2
            area = np.sum(component_mask)
            
            components_info.append({
                'label': i,
                'center_x': center_x,
                'center_y': center_y,
                'min_x': min_x,
                'max_x': max_x,
                'min_y': min_y,
                'max_y': max_y,
                'area': area,
                'mask': component_mask
            })
        
        if not components_info:
            # No valid components found
            return (torch.zeros_like(mask),)
        
        # Sort components based on sort_order
        if sort_order == "left-right":
            components_info.sort(key=lambda x: x['center_x'])
        elif sort_order == "right-left":
            components_info.sort(key=lambda x: x['center_x'], reverse=True)
        elif sort_order == "top-bottom":
            components_info.sort(key=lambda x: x['center_y'])
        elif sort_order == "bottom-top":
            components_info.sort(key=lambda x: x['center_y'], reverse=True)
        elif sort_order == "largest-smallest":
            components_info.sort(key=lambda x: x['area'], reverse=True)
        elif sort_order == "smallest-largest":
            components_info.sort(key=lambda x: x['area'])
        
        # Check if index is valid
        if index >= len(components_info):
            # Index out of range, return empty mask
            return (torch.zeros_like(mask),)
        
        # Get the selected component
        selected_component = components_info[index]
        
        # Create output mask with same dimensions as input
        result_np = np.zeros_like(source_np)
        
        # Copy values from source mask where the selected component exists
        component_coords = np.where(selected_component['mask'])
        result_np[component_coords] = source_np[component_coords]
        
        # Convert back to tensor with same shape as input
        if len(mask.shape) == 3:
            result_tensor = torch.from_numpy(result_np).unsqueeze(0).to(mask.device, dtype=mask.dtype)
        else:
            result_tensor = torch.from_numpy(result_np).to(mask.device, dtype=mask.dtype)
        
        return (result_tensor,)

class WCBoxMask:
    """
    Creates a box mask with dimensions matching the input image.
    """
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "image": ("IMAGE",),
                "x": ("FLOAT", {"default": 0.0, "min": 0.0, "max": 1.0, "step": 0.05, "round": 0.0001, "tooltip": "The x position of the mask as a percentage of the image width."}),
                "y": ("FLOAT", {"default": 0.0, "min": 0.0, "max": 1.0, "step": 0.05, "round": 0.0001, "tooltip": "The y position of the mask as a percentage of the image height."}),
                "width": ("FLOAT", {"default": 0.5, "min": 0.0, "max": 1.0, "step": 0.05, "round": 0.0001, "tooltip": "The width of the mask as a percentage of the image width."}),
                "height": ("FLOAT", {"default": 0.5, "min": 0.0, "max": 1.0, "step": 0.05, "round": 0.0001, "tooltip": "The height of the mask as a percentage of the image height."}),
                "strength": ("FLOAT", {"default": 1.0, "min": 0.0, "max": 1.0, "tooltip": "The strength of the mask, ie the value of all masked pixels, leaving the rest black ie 0."}),
            }
        }

    RETURN_TYPES = ("MASK",)
    RETURN_NAMES = ("mask",)
    FUNCTION = "create_box_mask"
    CATEGORY = "WC/masks"

    def create_box_mask(self, image, x, y, width, height, strength):
        """
        Creates a box mask with the same dimensions as the input image.
        
        Args:
            image: Input image to get dimensions from
            x, y: Position as percentage (0.0-1.0) 
            width, height: Size as percentage (0.0-1.0)
            strength: Mask value for the box area
        
        Returns:
            A mask tensor with the same height/width as the input image
        """
        # Get image dimensions - image is typically (batch, height, width, channels)
        if len(image.shape) == 4:
            batch_size, img_height, img_width, channels = image.shape
        elif len(image.shape) == 3:
            img_height, img_width, channels = image.shape
            batch_size = 1
        else:
            raise ValueError(f"Unexpected image shape: {image.shape}")
        
        # Create mask with same height/width as image
        mask = torch.zeros((img_height, img_width), dtype=torch.float32, device=image.device)
        
        # Calculate pixel coordinates from percentages
        start_x = int(x * img_width)
        start_y = int(y * img_height)
        end_x = int((x + width) * img_width)
        end_y = int((y + height) * img_height)
        
        # Clamp coordinates to image bounds
        start_x = max(0, min(start_x, img_width))
        start_y = max(0, min(start_y, img_height))
        end_x = max(0, min(end_x, img_width))
        end_y = max(0, min(end_y, img_height))
        
        # Fill the box area with the specified strength
        if end_x > start_x and end_y > start_y:
            mask[start_y:end_y, start_x:end_x] = strength
        
        # Return mask with batch dimension
        return (mask.unsqueeze(0),)

class WCBoundingBoxMask:
    """
    Creates a bounding box mask from an input mask. Finds the bounding box of all non-zero pixels
    in the input mask and returns a mask where everything inside the bounding box is 1 and everything outside is 0.
    """
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "mask": ("MASK",),
            }
        }

    RETURN_TYPES = ("MASK",)
    RETURN_NAMES = ("mask",)
    FUNCTION = "create_bounding_box_mask"
    CATEGORY = "WC/masks"

    def create_bounding_box_mask(self, mask):
        """
        Creates a bounding box mask from the input mask.
        
        Args:
            mask: Input mask tensor to find bounding box for
        
        Returns:
            A mask tensor where the bounding box area is filled with 1.0 and everything else is 0.0
        """
        # Handle batch dimension - mask is typically (batch, height, width)
        if len(mask.shape) == 3:
            batch_size, height, width = mask.shape
        elif len(mask.shape) == 2:
            height, width = mask.shape
            batch_size = 1
            mask = mask.unsqueeze(0)
        else:
            raise ValueError(f"Unexpected mask shape: {mask.shape}")
        
        # Process each mask in the batch
        output_masks = []
        for i in range(batch_size):
            current_mask = mask[i]
            
            # Find non-zero pixels
            nonzero_indices = torch.nonzero(current_mask > 0, as_tuple=False)
            
            if len(nonzero_indices) == 0:
                # If mask is empty, return empty mask
                bbox_mask = torch.zeros((height, width), dtype=torch.float32, device=mask.device)
            else:
                # Find bounding box coordinates
                min_y = torch.min(nonzero_indices[:, 0]).item()
                max_y = torch.max(nonzero_indices[:, 0]).item()
                min_x = torch.min(nonzero_indices[:, 1]).item()
                max_x = torch.max(nonzero_indices[:, 1]).item()
                
                # Create bounding box mask
                bbox_mask = torch.zeros((height, width), dtype=torch.float32, device=mask.device)
                bbox_mask[min_y:max_y+1, min_x:max_x+1] = 1.0
            
            output_masks.append(bbox_mask)
        
        # Stack back into batch format
        result = torch.stack(output_masks, dim=0)
        return (result,)


class WCCircleMask:
    """
    Creates a circle mask with dimensions matching the input image.
    """
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "image": ("IMAGE",),
                "x": ("FLOAT", {"default": 0.5, "min": 0.0, "max": 1.0, "step": 0.05, "round": 0.0001, "tooltip": "The x position of the circle center as a percentage of the image width."}),
                "y": ("FLOAT", {"default": 0.5, "min": 0.0, "max": 1.0, "step": 0.05, "round": 0.0001, "tooltip": "The y position of the circle center as a percentage of the image height."}),
                "radius": ("FLOAT", {"default": 0.2, "min": 0.0, "max": 1.0, "step": 0.05, "round": 0.0001, "tooltip": "The radius of the circle as a percentage of the smaller image dimension."}),
                "strength": ("FLOAT", {"default": 1.0, "min": 0.0, "max": 1.0, "tooltip": "The strength of the mask, ie the value of all masked pixels, leaving the rest black ie 0."}),
            }
        }

    RETURN_TYPES = ("MASK",)
    RETURN_NAMES = ("mask",)
    FUNCTION = "create_circle_mask"
    CATEGORY = "WC/masks"

    def create_circle_mask(self, image, x, y, radius, strength):
        """
        Creates a circle mask with the same dimensions as the input image.
        
        Args:
            image: Input image to get dimensions from
            x, y: Center position as percentage (0.0-1.0) 
            radius: Radius as percentage (0.0-1.0) of smaller dimension
            strength: Mask value for the circle area
        
        Returns:
            A mask tensor with the same height/width as the input image
        """
        # Get image dimensions - image is typically (batch, height, width, channels)
        if len(image.shape) == 4:
            batch_size, img_height, img_width, channels = image.shape
        elif len(image.shape) == 3:
            img_height, img_width, channels = image.shape
            batch_size = 1
        else:
            raise ValueError(f"Unexpected image shape: {image.shape}")
        
        # Create mask with same height/width as image
        mask = torch.zeros((img_height, img_width), dtype=torch.float32, device=image.device)
        
        # Calculate pixel coordinates from percentages
        center_x = x * img_width
        center_y = y * img_height
        # Use smaller dimension for radius calculation to maintain circular shape
        pixel_radius = radius * min(img_width, img_height)
        
        # Create coordinate grids
        y_coords, x_coords = torch.meshgrid(
            torch.arange(img_height, dtype=torch.float32, device=image.device),
            torch.arange(img_width, dtype=torch.float32, device=image.device),
            indexing='ij'
        )
        
        # Calculate normalized distances to create true circles regardless of aspect ratio
        # Normalize coordinates to [0,1] range to account for different image dimensions
        norm_x = (x_coords - center_x) / min(img_width, img_height)
        norm_y = (y_coords - center_y) / min(img_width, img_height)
        distances = torch.sqrt(norm_x ** 2 + norm_y ** 2)
        
        # Create circle mask - pixels within radius get the strength value
        circle_mask = (distances <= radius).float() * strength
        
        # Return mask with batch dimension
        return (circle_mask.unsqueeze(0),)


class WCBoundingCircleMask:
    """
    Creates a bounding circle mask from an input mask. Finds the smallest circle that contains all non-zero pixels
    in the input mask and returns a mask where everything inside the circle is 1 and everything outside is 0.
    """
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "mask": ("MASK",),
            }
        }

    RETURN_TYPES = ("MASK",)
    RETURN_NAMES = ("mask",)
    FUNCTION = "create_bounding_circle_mask"
    CATEGORY = "WC/masks"

    def create_bounding_circle_mask(self, mask):
        """
        Creates a bounding circle mask from the input mask.
        
        Args:
            mask: Input mask tensor to find bounding circle for
        
        Returns:
            A mask tensor where the bounding circle area is filled with 1.0 and everything else is 0.0
        """
        # Handle batch dimension
        if len(mask.shape) == 3:
            batch_size, height, width = mask.shape
            output_mask = torch.zeros_like(mask)
            
            for b in range(batch_size):
                single_mask = mask[b]
                
                # Find non-zero pixels
                nonzero_indices = torch.nonzero(single_mask, as_tuple=False)
                
                if len(nonzero_indices) > 0:
                    # Convert to float for calculations
                    points = nonzero_indices.float()
                    
                    # Find center as centroid of all non-zero pixels
                    center_y = torch.mean(points[:, 0])
                    center_x = torch.mean(points[:, 1])
                    
                    # Find maximum distance from center to any non-zero pixel
                    distances = torch.sqrt((points[:, 1] - center_x) ** 2 + (points[:, 0] - center_y) ** 2)
                    radius = torch.max(distances)
                    
                    # Create coordinate grids
                    y_coords, x_coords = torch.meshgrid(
                        torch.arange(height, dtype=torch.float32, device=mask.device),
                        torch.arange(width, dtype=torch.float32, device=mask.device),
                        indexing='ij'
                    )
                    
                    # Calculate distance from center for each pixel
                    pixel_distances = torch.sqrt((x_coords - center_x) ** 2 + (y_coords - center_y) ** 2)
                    
                    # Create circle mask
                    output_mask[b] = (pixel_distances <= radius).float()
                
        elif len(mask.shape) == 2:
            height, width = mask.shape
            output_mask = torch.zeros_like(mask)
            
            # Find non-zero pixels
            nonzero_indices = torch.nonzero(mask, as_tuple=False)
            
            if len(nonzero_indices) > 0:
                # Convert to float for calculations
                points = nonzero_indices.float()
                
                # Find center as centroid of all non-zero pixels
                center_y = torch.mean(points[:, 0])
                center_x = torch.mean(points[:, 1])
                
                # Find maximum distance from center to any non-zero pixel
                distances = torch.sqrt((points[:, 1] - center_x) ** 2 + (points[:, 0] - center_y) ** 2)
                radius = torch.max(distances)
                
                # Create coordinate grids
                y_coords, x_coords = torch.meshgrid(
                    torch.arange(height, dtype=torch.float32, device=mask.device),
                    torch.arange(width, dtype=torch.float32, device=mask.device),
                    indexing='ij'
                )
                
                # Calculate distance from center for each pixel
                pixel_distances = torch.sqrt((x_coords - center_x) ** 2 + (y_coords - center_y) ** 2)
                
                # Create circle mask
                output_mask = (pixel_distances <= radius).float()
            
            # Add batch dimension
            output_mask = output_mask.unsqueeze(0)
        else:
            raise ValueError(f"Unexpected mask shape: {mask.shape}")
        
        return (output_mask,)

NODE_CLASS_MAPPINGS = {
    "WCCompositeMask": WCCompositeMask,
    "WCMaskBounds": WCMaskBounds,
    "WCSkipIfMaskEmpty": WCSkipIfMaskEmpty,
    "WCSeparateMaskComponents": WCSeparateMaskComponents,
    "WCBoxMask": WCBoxMask,
    "WCBoundingBoxMask": WCBoundingBoxMask,
    "WCCircleMask": WCCircleMask,
    "WCBoundingCircleMask": WCBoundingCircleMask,
}